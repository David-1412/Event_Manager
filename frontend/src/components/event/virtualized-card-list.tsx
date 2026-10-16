"use client";

import { useCallback, useEffect, useRef, useState } from "react";


/**
 * Fixed row height the windowing math is built on.
 *
 * The compact browse card is a single dense row of fixed content (title, three
 * meta lines, a social-proof line, a footer), so it lands on this height within a
 * few pixels. A fixed number is not just an optimisation, it is what lets the
 * scroll range be computed up front; the overscan below is what absorbs the
 * remainder when a two-line title or a wrapped badge row pushes a card past it.
 *
 * The number is the card height plus its share of the list gap, so it has to move
 * with the thumbnail. At the old 56px thumb the text column measured ~120px and
 * the row was 108; the 176px thumb (`lg:w-44`) is now the taller side, and the
 * tightened gap-0.5/pt-1.5 rhythm keeps the text under it (176 + 24 padding + 2
 * border + 8 gap = 210, plus headroom for a wrapped title). Rows below `lg` are
 * ~152px, so on a narrow desktop this over-reserves - which only ever renders
 * more cards than needed, never fewer, so the scrollbar runs slightly long
 * rather than the window losing rows.
 */
export const ROW_HEIGHT = 240;

/** Rows rendered beyond the viewport in each direction, so a fast flick never
 *  reveals bare padding where a card should be. */
const OVERSCAN = 6;

interface VirtualizedCardListProps<T> {
  /** Stable ids, in display order. Only the ids are compared, never the payloads. */
  ids: readonly string[];
  /** Payload lookup. A missing id renders nothing rather than a stale card. */
  data: Map<string, T>;
  renderItem: (id: string, data: T | null, index: number) => React.ReactNode;
  className?: string;
}

/**
 * A windowed vertical list.
 *
 * Used because the browse feed is capped at 100 rows (EventQueryModel.MaxPageSize)
 * and rendering 100 cards, each with an image and a join control, measurably slows
 * the first paint and every selection re-render. Below the caller's threshold the
 * plain map is cheaper than this, so the caller picks.
 *
 * Deliberately not an accessible listbox: each row is whatever `renderItem`
 * returns, so the cards keep their own roles and tab order, and scrolling is the
 * ordinary page scroll rather than arrow-key navigation that would have to be
 * announced and maintained.
 *
 * The scroll container is the nearest scrollable ancestor, resolved at runtime
 * rather than assumed. `/` scrolls the page below `lg` and an inner column above
 * it, and a hardcoded `window` listener would silently stop windowing the moment
 * the layout changed.
 */
export function VirtualizedCardList<T>({
  ids,
  data,
  renderItem,
  className,
}: VirtualizedCardListProps<T>) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const [range, setRange] = useState({ start: 0, end: Math.min(ids.length, 30) });

  /** Nearest vertically scrollable ancestor, or the page itself. */
  const findScroller = useCallback((): HTMLElement | Window => {
    let node = hostRef.current?.parentElement ?? null;
    while (node && node !== document.body) {
      const { overflowY } = getComputedStyle(node);
      if (overflowY === "auto" || overflowY === "scroll") return node;
      node = node.parentElement;
    }
    return window;
  }, []);

  useEffect(() => {
    const scroller = findScroller();
    const host = hostRef.current;
    if (!host) return;

    let frame = 0;
    const measure = () => {
      frame = 0;
      const top =
        scroller === window
          ? window.scrollY
          : (scroller as HTMLElement).scrollTop;
      // Position within the scrollable content, so the math is the same whether
      // the page or an inner column is doing the scrolling.
      const hostTop =
        scroller === window
          ? host.getBoundingClientRect().top + window.scrollY
          : host.getBoundingClientRect().top -
            (scroller as HTMLElement).getBoundingClientRect().top +
            (scroller as HTMLElement).scrollTop;
      const viewport =
        scroller === window ? window.innerHeight : (scroller as HTMLElement).clientHeight;
      const relative = Math.max(0, top - hostTop);

      const start = Math.max(0, Math.floor(relative / ROW_HEIGHT) - OVERSCAN);
      const visible = Math.ceil(viewport / ROW_HEIGHT) + OVERSCAN * 2;
      const end = Math.min(ids.length, start + visible);
      setRange((prev) => (prev.start === start && prev.end === end ? prev : { start, end }));
    };

    const onScroll = () => {
      // One measure per frame at most. Scroll events fire far faster than paint
      // and this runs on every pixel of a flick through 100 events.
      if (frame) return;
      frame = requestAnimationFrame(measure);
    };

    measure();
    scroller.addEventListener("scroll", onScroll, { passive: true });
    window.addEventListener("resize", onScroll);
    return () => {
      if (frame) cancelAnimationFrame(frame);
      scroller.removeEventListener("scroll", onScroll);
      window.removeEventListener("resize", onScroll);
    };
  }, [findScroller, ids.length]);

  // A shorter result set can leave the window past the end; clamp rather than
  // render an empty slice and look like the list lost its contents.
  const start = Math.min(range.start, Math.max(0, ids.length - 1));
  const end = Math.min(range.end, ids.length);

  return (
    <div ref={hostRef} className={className}>
      {/* Reserve the full height so the scrollbar reflects the real list length
          and a jump to the bottom lands where the user expects. */}
      <div style={{ height: ids.length * ROW_HEIGHT }} className="relative">
        <div
          className="absolute inset-x-0 top-0"
          style={{ transform: `translateY(${start * ROW_HEIGHT}px)` }}
        >
          {ids.slice(start, end).map((id, i) => (
            <div key={id} style={{ height: ROW_HEIGHT }}>
              {renderItem(id, data.get(id) ?? null, start + i)}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
