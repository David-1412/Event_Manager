"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState } from "react";
import { Search, X } from "lucide-react";
import { useSearchParamsValue } from "@/lib/query";
import { cn } from "@/lib/cn";

export type SearchType = "all" | "events" | "posts";

/** Which page the search is on, which decides what it shows before anyone types.
 * "other" is a page with no search of its own (My Events, a profile): it starts on
 * All and never carries that page's parameters into a search. */
export type SearchContext = "events" | "forum" | "search" | "other";

const CHIPS: { value: SearchType; label: string }[] = [
  { value: "all", label: "All" },
  { value: "events", label: "Events" },
  { value: "posts", label: "Posts" },
];

/**
 * Where a search goes. Each chip searches where its results already live:
 *  - Events filters the Events page (`/?q=`), keeping its other filters;
 *  - Posts filters the Forum (`/forum?q=`), keeping category, sort and tab;
 *  - All opens the combined results page (`/search?q=&type=all`).
 * So "Events" is the same search the Events page always had, and only "All" needs a
 * page of its own.
 */
export function searchHref(type: SearchType, q: string, current: URLSearchParams, context: SearchContext): string {
  const text = q.trim();
  if (type === "all") {
    return text ? `/search?q=${encodeURIComponent(text)}` : context === "forum" ? "/forum" : "/";
  }
  const path = type === "events" ? "/" : "/forum";
  // Keep the page's own filters when searching within the page you are on.
  const keep = (context === "events" && type === "events") || (context === "forum" && type === "posts");
  const params = keep ? new URLSearchParams(current) : new URLSearchParams();
  if (text) params.set("q", text);
  else params.delete("q");
  const search = params.toString();
  return search ? `${path}?${search}` : path;
}

/** The page's search context, from its path. */
export function contextFromPath(pathname: string | null): SearchContext {
  if (pathname === "/") return "events";
  if (pathname === "/forum") return "forum";
  if (pathname === "/search") return "search";
  return "other";
}

/** The chip a page starts on: the page's own content when it is already filtered by
 * a search, otherwise All. */
function initialType(context: SearchContext, params: URLSearchParams): SearchType {
  if (context === "search") {
    const type = params.get("type");
    return type === "events" || type === "posts" ? type : "all";
  }
  if (context === "other" || !params.get("q")) return "all";
  return context === "events" ? "events" : "posts";
}

/**
 * The global search, in the header: one box for events and forum posts.
 *
 * From `lg` up it sits inline between the navigation and the account menu; below
 * that it collapses to a magnifying glass that opens a full-width search row under
 * the header. Either way, focusing the box opens a small panel with the
 * All / Events / Posts chips - picking one with something typed searches at once in
 * that scope, and Enter searches in the selected one.
 *
 * The box follows the URL: on a page already filtered by a search it shows that
 * search, with the matching chip selected, so the header always says what the page
 * below it is showing.
 */
export function HeaderSearch() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParamsValue();
  const context = contextFromPath(pathname);
  const urlQ = context === "other" ? "" : (params.get("q") ?? "");

  const [text, setText] = useState(urlQ);
  const [type, setType] = useState<SearchType>(() => initialType(context, params));
  const [focused, setFocused] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const desktopInput = useRef<HTMLInputElement>(null);
  const mobileInput = useRef<HTMLInputElement>(null);
  const ids = { desktop: useId(), mobile: useId(), chips: useId() };

  // A navigation, Back or a link changes the search in the URL; the box follows it.
  useEffect(() => {
    queueMicrotask(() => {
      setText(urlQ);
      setType(initialType(context, params));
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlQ, context]);

  useEffect(() => {
    if (mobileOpen) mobileInput.current?.focus();
  }, [mobileOpen]);

  function go(nextType: SearchType, nextText: string) {
    const href = searchHref(nextType, nextText, params, context);
    const url = new URL(href, window.location.origin);
    if (url.pathname === window.location.pathname) {
      // Same page: update the URL in place the way the pages' own filters do, so the
      // list re-reads it without a navigation.
      window.history.pushState(null, "", href);
      window.dispatchEvent(new PopStateEvent("popstate"));
    } else {
      router.push(href);
    }
  }

  function submit() {
    go(type, text);
    setFocused(false);
    setMobileOpen(false);
    desktopInput.current?.blur();
  }

  function pickChip(next: SearchType) {
    setType(next);
    // "Applies immediately": with something typed, a chip is a new search.
    if (text.trim()) go(next, text);
  }

  function clear() {
    setText("");
    if (urlQ) go(type, "");
  }

  const box = (id: string, ref: React.RefObject<HTMLInputElement | null>, autoClose: boolean) => (
    <form
      role="search"
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
      className="relative"
      onFocus={() => setFocused(true)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) {
          setFocused(false);
          if (autoClose && !text.trim()) setMobileOpen(false);
        }
      }}
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          setFocused(false);
          setMobileOpen(false);
          ref.current?.blur();
        }
      }}
    >
      <label htmlFor={id} className="sr-only">
        Search events and posts
      </label>
      <Search aria-hidden size={16} className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-fg-muted" />
      <input
        ref={ref}
        id={id}
        type="search"
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder="Search events and posts..."
        maxLength={100}
        aria-controls={ids.chips}
        className="h-10 w-full rounded-full border border-border bg-surface-2 pl-10 pr-10 text-meta text-fg placeholder:text-fg-muted transition-colors focus:border-brand-600 focus:bg-surface focus:outline-none [&::-webkit-search-cancel-button]:hidden"
      />
      {text && (
        <button
          type="button"
          aria-label="Clear search"
          onClick={clear}
          className="press absolute right-1.5 top-1/2 flex h-7 w-7 -translate-y-1/2 items-center justify-center rounded-full text-fg-muted hover:bg-surface-2 hover:text-fg"
        >
          <X aria-hidden size={15} />
        </button>
      )}

      {focused && (
        <div
          id={ids.chips}
          className="absolute left-0 right-0 top-full z-40 mt-2 rounded-lg border border-border bg-surface p-3 shadow-float"
        >
          <div role="radiogroup" aria-label="Search in" className="flex gap-2">
            {CHIPS.map((chip) => (
              <button
                key={chip.value}
                type="button"
                role="radio"
                aria-checked={type === chip.value}
                // mousedown would otherwise blur the input and close this panel
                // before the click lands.
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => pickChip(chip.value)}
                className={cn(
                  "press h-8 rounded-full border px-3 text-meta font-medium transition-colors",
                  type === chip.value
                    ? "border-brand-600 bg-brand-tint text-brand-600"
                    : "border-border bg-surface text-fg-muted hover:text-fg",
                )}
              >
                {chip.label}
              </button>
            ))}
          </div>
          <p className="mt-2 text-micro text-fg-muted">
            {text.trim()
              ? `Press Enter to search ${type === "all" ? "everything" : type}.`
              : "Type to search, then press Enter."}
          </p>
        </div>
      )}
    </form>
  );

  return (
    <>
      {/* lg and up: inline, flexible, capped so it never crowds the navigation. */}
      <div className="hidden min-w-0 max-w-md flex-1 lg:block">{box(ids.desktop, desktopInput, false)}</div>

      {/* Below lg: a magnifying glass that opens a full-width row under the header. */}
      <button
        type="button"
        onClick={() => setMobileOpen((open) => !open)}
        aria-label={mobileOpen ? "Close search" : "Search"}
        aria-expanded={mobileOpen}
        className="press inline-flex h-9 w-9 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg lg:hidden"
      >
        {mobileOpen ? <X aria-hidden size={20} /> : <Search aria-hidden size={20} />}
      </button>
      {mobileOpen && (
        <div className="absolute left-0 right-0 top-full border-b border-border bg-surface px-4 py-3 shadow-raise lg:hidden">
          {box(ids.mobile, mobileInput, true)}
        </div>
      )}
    </>
  );
}
