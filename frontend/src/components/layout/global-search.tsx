"use client";

import { useRouter } from "next/navigation";
import { useEffect, useId, useState } from "react";
import { Search, X } from "lucide-react";
import { useSearchParamsValue } from "@/lib/query";
import { cn } from "@/lib/cn";

export type SearchType = "all" | "events" | "posts";

/** Which page the bar sits on, which decides what it shows before anyone types.
 * "other" is a page with no search of its own (My Events): it starts on All and
 * never carries that page's parameters into a search. */
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
 * The search bar at the top of Events, Forum and the results page. One box for both
 * events and forum posts, with All / Events / Posts chips under it.
 */
export function GlobalSearch({ context, className }: { context: SearchContext; className?: string }) {
  const router = useRouter();
  const params = useSearchParamsValue();
  const urlQ = context === "other" ? "" : (params.get("q") ?? "");
  const [text, setText] = useState(urlQ);
  const [type, setType] = useState<SearchType>(() => initialType(context, params));
  const inputId = useId();

  // Back/forward or a link changes the search in the URL; the box follows it.
  useEffect(() => {
    queueMicrotask(() => {
      setText(urlQ);
      setType(initialType(context, params));
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlQ, context]);

  function go(nextType: SearchType, nextText: string) {
    const href = searchHref(nextType, nextText, params, context);
    const url = new URL(href, window.location.origin);
    if (url.pathname === window.location.pathname) {
      // Same page: update the URL in place the way Browse's own filters do, so the
      // list re-reads it without a navigation.
      window.history.pushState(null, "", href);
      window.dispatchEvent(new PopStateEvent("popstate"));
    } else {
      router.push(href);
    }
  }

  function pickChip(next: SearchType) {
    setType(next);
    // "Filter instantly": with something typed, a chip is a new search.
    if (text.trim()) go(next, text);
  }

  return (
    <form
      role="search"
      onSubmit={(e) => {
        e.preventDefault();
        go(type, text);
      }}
      className={cn("flex flex-col gap-2", className)}
    >
      <label htmlFor={inputId} className="sr-only">
        Search events and posts
      </label>
      <div className="relative">
        <Search
          aria-hidden
          size={18}
          className="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-fg-muted"
        />
        <input
          id={inputId}
          type="search"
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="Search events and posts"
          maxLength={100}
          className="h-12 w-full rounded-full border border-border bg-surface-2 pl-11 pr-24 text-body text-fg placeholder:text-fg-muted focus:border-brand-600 focus:bg-surface focus:outline-none [&::-webkit-search-cancel-button]:hidden"
        />
        {text && (
          <button
            type="button"
            aria-label="Clear search"
            onClick={() => {
              setText("");
              if (urlQ) go(type, "");
            }}
            className="press absolute right-14 top-1/2 flex h-8 w-8 -translate-y-1/2 items-center justify-center rounded-full text-fg-muted hover:bg-surface hover:text-fg"
          >
            <X aria-hidden size={16} />
          </button>
        )}
        <button
          type="submit"
          aria-label="Search"
          className="press absolute right-1.5 top-1/2 flex h-9 w-11 -translate-y-1/2 items-center justify-center rounded-full bg-brand-600 text-brand-fg hover:bg-brand-700"
        >
          <Search aria-hidden size={18} />
        </button>
      </div>

      <div role="radiogroup" aria-label="Search in" className="flex gap-2">
        {CHIPS.map((chip) => (
          <button
            key={chip.value}
            type="button"
            role="radio"
            aria-checked={type === chip.value}
            onClick={() => pickChip(chip.value)}
            className={cn(
              "press h-8 rounded-full border px-3 text-meta font-medium",
              type === chip.value
                ? "border-brand-600 bg-brand-tint text-brand-600"
                : "border-border bg-surface text-fg-muted hover:text-fg",
            )}
          >
            {chip.label}
          </button>
        ))}
      </div>
    </form>
  );
}
