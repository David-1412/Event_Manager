"use client";

import Link from "next/link";
import { useEffect, useId, useState } from "react";
import { Flame } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Dialog } from "@/components/ui/dialog";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { trendingHref } from "@/features/forum/forum-api";
import { useTrending } from "@/features/forum/use-forum";
import { cn } from "@/lib/cn";
import { useMediaQuery } from "@/lib/use-media-query";
import type { TrendingItem, TrendingWindow } from "@/types/forum";

/** Rows visible in the rolling widget. */
export const VISIBLE_ROWS = 3;
/** How often the widget advances by one row. */
export const ROLL_INTERVAL_MS = 1000;

/** The list the widget rolls through. A week rather than "today" so the widget is
 * rarely empty; the "More" modal opens on Today for what is new right now. */
const WIDGET_WINDOW: TrendingWindow = "week";

/**
 * The rows on screen when the roll is at `start`: `size` consecutive items, wrapping
 * past the end - 1,2,3 then 2,3,4 ... 9,10,1 then 10,1,2. Ranks are the items' real
 * positions, so a wrapped row still says "1". A list no longer than `size` is
 * returned whole and never rolls: rolling three items through three slots would only
 * reshuffle the same rows.
 */
export function rollingRows<T>(items: readonly T[], start: number, size = VISIBLE_ROWS) {
  if (items.length <= size) return items.map((item, i) => ({ item, rank: i + 1 }));
  return Array.from({ length: size }, (_, k) => {
    const index = (((start + k) % items.length) + items.length) % items.length;
    return { item: items[index], rank: index + 1 };
  });
}

/**
 * Monash Pulse (Campus Buzz): the campus top 10 - events and forum posts ranked by
 * total page views (the flame number, which never goes down) - rolling one row per
 * second.
 *
 * The roll pauses while the pointer or keyboard focus is inside the widget, because
 * a link that moves out from under the cursor every second cannot be clicked, and
 * it does not run at all for people who ask their system to reduce motion; they see
 * the top three, still. Both are the WCAG "pause, stop, hide" rule for content that
 * moves on its own for more than five seconds.
 */
export function TrendingBanner({ className }: { className?: string }) {
  const { items, isLoading } = useTrending(WIDGET_WINDOW);
  const [start, setStart] = useState(0);
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const [open, setOpen] = useState(false);
  const reduceMotion = useMediaQuery("(prefers-reduced-motion: reduce)");
  const titleId = useId();

  const count = items?.length ?? 0;
  const rolling = count > VISIBLE_ROWS && !hovered && !focused && !open && !reduceMotion;

  useEffect(() => {
    if (!rolling) return;
    const timer = window.setInterval(() => setStart((s) => (s + 1) % count), ROLL_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [rolling, count]);

  const rows = items ? rollingRows(items, count ? start % count : 0) : [];

  return (
    <Card
      className={cn("overflow-hidden p-4", className)}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setFocused(false);
      }}
    >
      <section aria-labelledby={titleId}>
        <div className="flex items-center gap-2">
          <Flame
            aria-hidden
            size={20}
            className="fill-red-500 text-red-500 drop-shadow-[0_0_6px_rgb(239_68_68/0.6)]"
          />
          <h2 id={titleId} className="text-h3 text-fg">
            Monash Pulse <span className="text-fg-muted">(Campus Buzz)</span>
          </h2>
          <button
            type="button"
            onClick={() => setOpen(true)}
            disabled={count === 0}
            className="press ml-auto h-8 rounded-full px-3 text-meta font-medium text-danger hover:bg-danger-tint disabled:text-fg-muted disabled:hover:bg-transparent"
          >
            More
          </button>
        </div>

        <div className="mt-3">
          {isLoading && !items ? (
            <div className="flex flex-col gap-2" aria-busy>
              {[0, 1, 2].map((i) => (
                <SkeletonBlock key={i} className="h-6 w-full" />
              ))}
            </div>
          ) : count === 0 ? (
            <p className="text-meta text-fg-muted">
              Nothing trending yet. Open an event or a post and it starts counting.
            </p>
          ) : (
            // Not a live region: announcing a new row every second would talk over
            // everything else on the page. The full list is in the modal.
            <ol className="flex flex-col gap-1">
              {rows.map(({ item, rank }) => (
                <TrendingRow key={`${rank}-${item.id}`} item={item} rank={rank} />
              ))}
            </ol>
          )}
        </div>
      </section>

      <Dialog open={open} onClose={() => setOpen(false)} labelledBy={`${titleId}-modal`}>
        {open && <TrendingModal titleId={`${titleId}-modal`} onNavigate={() => setOpen(false)} />}
      </Dialog>
    </Card>
  );
}

function TrendingRow({ item, rank }: { item: TrendingItem; rank: number }) {
  return (
    <li className="flex items-center gap-3 rounded-md px-1 py-1 hover:bg-surface-2">
      <Rank rank={rank} />
      <Link
        href={trendingHref(item)}
        className="min-w-0 flex-1 truncate text-body text-fg no-underline hover:text-danger"
      >
        {item.title}
      </Link>
      <span className="inline-flex shrink-0 items-center gap-1 text-meta font-semibold text-danger tabular-nums">
        <Flame aria-hidden size={14} className="fill-current" />
        {item.score.toLocaleString("en-AU")}
        <span className="sr-only"> views</span>
      </span>
    </li>
  );
}

function Rank({ rank }: { rank: number }) {
  return (
    <span
      className={cn(
        "w-6 shrink-0 text-center text-meta font-bold tabular-nums",
        rank <= 3 ? "text-danger" : "text-fg-muted",
      )}
    >
      {rank}
    </span>
  );
}

const TABS: { value: TrendingWindow; label: string }[] = [
  { value: "today", label: "Today" },
  { value: "3d", label: "Past 3 Days" },
  { value: "week", label: "This Week" },
];

/** The expanded list. Mounted only while the modal is open, so its three windows
 * are fetched on demand rather than with every forum visit. */
function TrendingModal({ titleId, onNavigate }: { titleId: string; onNavigate: () => void }) {
  const [tab, setTab] = useState<TrendingWindow>("today");
  const { items, isLoading } = useTrending(tab);

  return (
    <div className="flex flex-col gap-4">
      <h2 id={titleId} className="flex items-center gap-2 text-h2 text-fg">
        <Flame aria-hidden size={20} className="fill-red-500 text-red-500" />
        Top 10 Campus Trending
      </h2>

      <div role="tablist" aria-label="Time frame" className="flex gap-1 rounded-full bg-surface-2 p-1">
        {TABS.map((t) => (
          <button
            key={t.value}
            type="button"
            role="tab"
            aria-selected={tab === t.value}
            onClick={() => setTab(t.value)}
            className={cn(
              "press h-8 flex-1 rounded-full px-3 text-meta font-medium",
              tab === t.value ? "bg-surface text-danger shadow-raise" : "text-fg-muted hover:text-fg",
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div role="tabpanel" aria-label={TABS.find((t) => t.value === tab)?.label}>
        {isLoading && !items ? (
          <div className="flex flex-col gap-2" aria-busy>
            {[0, 1, 2, 3, 4].map((i) => (
              <SkeletonBlock key={i} className="h-9 w-full" />
            ))}
          </div>
        ) : !items || items.length === 0 ? (
          <p className="py-6 text-center text-meta text-fg-muted">Nothing trending in this period yet.</p>
        ) : (
          <ol className="flex flex-col divide-y divide-border">
            {items.map((item, i) => (
              <li key={`${item.kind}-${item.id}`} className="flex items-center gap-3 py-2.5">
                <Rank rank={i + 1} />
                <Link
                  href={trendingHref(item)}
                  onClick={onNavigate}
                  className="min-w-0 flex-1 text-body text-fg no-underline hover:text-danger"
                >
                  <span className="line-clamp-2">{item.title}</span>
                  <span className="text-micro uppercase text-fg-muted">
                    {item.kind === "event" ? "Event" : "Forum"}
                  </span>
                </Link>
                <span className="inline-flex shrink-0 items-center gap-1 rounded-full bg-danger-tint px-2.5 py-1 text-xs font-semibold text-danger tabular-nums">
                  <Flame aria-hidden size={13} className="fill-current" />
                  {item.score.toLocaleString("en-AU")} {item.score === 1 ? "view" : "views"}
                </span>
              </li>
            ))}
          </ol>
        )}
      </div>
    </div>
  );
}
