"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { request, usingFixtures } from "@/lib/api";
import { TAG_MAX_PER_EVENT, TAG_MIN_LENGTH, normalizeTag } from "@/lib/sports";
import type { Tag } from "@/types/events";

/**
 * Chip input with autocomplete over existing tags.
 *
 * Suggestions come from GET /api/tags/suggest, which only knows tags already in
 * use - there is no curated list. On a brand-new database that means no
 * suggestions at all, so a non-matching entry is never a dead end: the list offers
 * the normalized entry itself and the hint line says "Press Enter to create #xyz".
 * That affordance is what makes implicit tag creation usable rather than invisible.
 *
 * Chips are stored normalized, so "#Tennis" and "tennis" collapse to one chip the
 * way the server would, while the order a host built is preserved.
 */
export function TagInput({
  tags,
  onChange,
  error,
  id,
}: {
  tags: Tag[];
  onChange: (tags: Tag[]) => void;
  error?: string;
  id?: string;
}) {
  const [draft, setDraft] = useState("");
  const [open, setOpen] = useState(false);
  const [suggestions, setSuggestions] = useState<Tag[]>([]);
  const [highlight, setHighlight] = useState(0);
  const wrapRef = useRef<HTMLDivElement>(null);

  const normalizedDraft = normalizeTag(draft);
  const alreadyPicked = new Set(tags);
  const full = tags.length >= TAG_MAX_PER_EVENT;

  // Debounced: typing a five-letter tag is one request, not five. Skipped entirely
  // under fixtures, where there is no API to ask and a fetch would 404 in Vitest.
  // `tagKey` is the joined list hoisted to a plain string so the dependency array
  // stays a list of simple expressions (the exhaustive-deps rule rejects calls).
  const tagKey = tags.join(",");

  // The search is only meaningful while there is something to search for. Deriving
  // this instead of clearing state inside the effect keeps the effect to a single
  // job (talk to the API) and avoids a second synchronous render pass (the
  // react-hooks/set-state-in-effect rule exists for exactly that).
  const searching = Boolean(normalizedDraft) && !full && !usingFixtures;

  useEffect(() => {
    if (!searching || !normalizedDraft) return;

    let active = true;
    const timer = setTimeout(async () => {
      try {
        const found = await request<Tag[]>(
          `/api/tags/suggest?q=${encodeURIComponent(normalizedDraft)}&limit=8`,
        );
        if (active) setSuggestions(found.filter((t) => !alreadyPicked.has(t)));
      } catch {
        // Suggestions are a convenience; the field works without them.
        if (active) setSuggestions([]);
      }
    }, 200);
    return () => {
      active = false;
      clearTimeout(timer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searching, normalizedDraft, tagKey]);


  useEffect(() => {
    const onDocClick = (e: MouseEvent) => {
      if (!wrapRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, []);

  // Existing matches first, then the draft itself so Enter always has a target.
  // Gated on `searching` so a collapsed or completed search cannot leave stale
  // suggestions on screen, which is what the removed setSuggestions([]) used to do.
  const rows = useMemo(() => {
    const prior = searching ? suggestions.filter((t) => !alreadyPicked.has(t)) : [];
    const list: Tag[] = prior;
    if (normalizedDraft && !alreadyPicked.has(normalizedDraft)) list.push(normalizedDraft);
    return list.slice(0, 8);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searching, suggestions, normalizedDraft, tagKey]);


  function commit(raw: string) {
    const tag = normalizeTag(raw);
    if (!tag || tags.includes(tag) || tags.length >= TAG_MAX_PER_EVENT) {
      setDraft("");
      return;
    }
    onChange([...tags, tag]);
    setDraft("");
    setSuggestions([]);
  }

  function remove(tag: Tag) {
    onChange(tags.filter((t) => t !== tag));
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter" || e.key === ",") {
      e.preventDefault();
      // Clamped here rather than in an effect: rows can only change while typing,
      // and a stale index must not select out of bounds on commit.
      const index = Math.min(highlight, Math.max(rows.length - 1, 0));
      commit(rows[index] ?? draft);
      setHighlight(0);
      setOpen(false);
      return;
    }

    if (e.key === "Backspace" && !draft && tags.length) {
      remove(tags[tags.length - 1]);
      return;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setOpen(true);
      setHighlight((h) => Math.min(h + 1, Math.max(rows.length - 1, 0)));
      return;
    }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      setHighlight((h) => Math.max(h - 1, 0));
      return;
    }
    if (e.key === "Escape") setOpen(false);
  };

  const canCreate =
    normalizedDraft !== null && !alreadyPicked.has(normalizedDraft) && !full;

  return (
    <div ref={wrapRef} className="relative">
      <div
        className={`flex flex-wrap items-center gap-1.5 rounded-md border bg-surface px-2 py-1.5 ${
          error ? "border-danger-500" : "border-border"
        }`}
      >
        {tags.map((tag) => (
          <span
            key={tag}
            className="inline-flex items-center gap-1 rounded-full bg-surface-2 px-2 py-0.5 text-meta text-fg"
          >
            #{tag}
            <button
              type="button"
              aria-label={`Remove ${tag}`}
              onClick={() => remove(tag)}
              className="text-fg-muted hover:text-fg"
            >
              {"\u00d7"}
            </button>
          </span>
        ))}
        <input
          id={id}
          value={draft}
          // Disabled once capped, so "at most 5" is enforced by the control rather
          // than by an error raised after the fact.
          disabled={full}
          onChange={(e) => {
            setDraft(e.target.value);
            setHighlight(0);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          placeholder={full ? `Max ${TAG_MAX_PER_EVENT} tags` : "e.g. tennis, club"}
          className="min-w-[8rem] flex-1 bg-transparent py-0.5 text-body text-fg outline-none placeholder:text-fg-muted disabled:cursor-not-allowed"
        />
      </div>

      {open && rows.length > 0 && (
        <ul
          role="listbox"
          className="absolute z-30 mt-1 max-h-56 w-full overflow-auto rounded-md border border-border bg-surface py-1 shadow-raise"
        >
          {rows.map((tag, i) => (
            <li key={tag} role="option" aria-selected={i === highlight}>
              <button
                type="button"
                onMouseEnter={() => setHighlight(i)}
                onClick={() => {
                  commit(tag);
                  setOpen(false);
                }}
                className={`flex w-full items-center justify-between px-3 py-1.5 text-left text-body ${
                  i === highlight ? "bg-surface-2" : ""
                }`}
              >
                #{tag}
                {!suggestions.includes(tag) && (
                  <span className="text-meta text-fg-muted">new</span>
                )}
              </button>
            </li>
          ))}
        </ul>
      )}

      {error ? (
        <p className="mt-1 text-meta text-danger-500">{error}</p>
      ) : (
        <p className="mt-1 text-meta text-fg-muted">
          {full
            ? `Up to ${TAG_MAX_PER_EVENT} tags`
            : canCreate
              ? `Press Enter to create #${normalizedDraft}`
              : `Letters and numbers, ${TAG_MIN_LENGTH}+ characters. Enter to add.`}
        </p>
      )}
    </div>
  );
}

