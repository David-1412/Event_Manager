"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { cn } from "@/lib/cn";

/**
 * 300ms debounce, `cmd|ctrl+/` focuses it, `Escape` clears it, never fetches
 * per keystroke (spec §7). `type="search"` keeps the native clear affordance.
 */
export function SearchInput({
  value,
  onChange,
  placeholder = "Search events",
  className,
  id = "event-search",
}: {
  value: string | null;
  onChange: (next: string | null) => void;
  placeholder?: string;
  className?: string;
  id?: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Adopt external changes (back button, Reset) without fighting the typist.
  // React's own "adjust state during render" pattern: comparing against the
  // previous prop and re-rendering immediately is cheaper and cleaner than an
  // effect that copies a prop into state.
  const [adopted, setAdopted] = useState(value ?? "");
  const [draft, setDraft] = useState(value ?? "");
  if (adopted !== (value ?? "")) {
    setAdopted(value ?? "");
    setDraft(value ?? "");
  }

  const commit = useCallback(
    (next: string) => {
      onChange(next.trim() ? next.trim() : null);
    },
    [onChange],
  );

  const schedule = useCallback(
    (next: string) => {
      if (timer.current) clearTimeout(timer.current);
      timer.current = setTimeout(() => commit(next), 300);
    },
    [commit],
  );

  useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "/" && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        inputRef.current?.focus();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  return (
    <div className={cn("relative flex-1", className)}>
      <svg
        viewBox="0 0 16 16"
        width="16"
        height="16"
        fill="none"
        aria-hidden="true"
        className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-muted"
      >
        <circle cx="7" cy="7" r="4.5" stroke="currentColor" strokeWidth="1.6" />
        <path d="m10.5 10.5 3 3" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
      </svg>
      <input
        ref={inputRef}
        id={id}
        type="search"
        value={draft}
        placeholder={placeholder}
        aria-keyshortcuts="Meta+/ Control+/"
        onChange={(e) => {
          setDraft(e.target.value);
          schedule(e.target.value);
        }}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            if (timer.current) clearTimeout(timer.current);
            commit(draft);
          }
          if (e.key === "Escape") {
            if (timer.current) clearTimeout(timer.current);
            setDraft("");
            commit("");
          }
        }}
        className="h-11 w-full rounded-md border border-border bg-surface pl-9 pr-16 text-body text-fg placeholder:text-fg-muted"
      />
      <kbd
        aria-hidden
        className="pointer-events-none absolute right-3 top-1/2 hidden -translate-y-1/2 rounded-sm border border-border bg-surface-2 px-1.5 py-0.5 text-micro text-fg-muted lg:block"
      >
        ⌘/
      </kbd>
    </div>
  );
}
