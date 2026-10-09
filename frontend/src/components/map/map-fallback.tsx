import type { EventListItem } from "@/types/events";
import { formatCardWhen, formatTagLine, formatTimeRange } from "@/lib/format";


/**
 * Stand-in for the map when no API key is configured or the script fails.
 * Deliberately the same box height as the live map so nothing shifts later, and
 * it points at the list rather than leaving a dead region (spec §9: "list still
 * fully usable").
 */
export function MapFallback({
  className,
  height,
  message = "Map unavailable - the list below has every event.",
}: {
  className?: string;
  height?: number;
  message?: string;
}) {
  const sizing = className?.includes("h-")
    ? className
    : `${className ?? ""} ${height ? `h-[${height}px]` : "h-full min-h-48"}`;
  return (
    <div
      data-map-fallback
      className={"flex w-full items-center justify-center bg-surface-2 p-4 " + sizing}
      style={height ? { height } : undefined}
      role="alert"
      tabIndex={-1}
      aria-label="Map unavailable"
    >
      <p className="max-w-60 text-center text-meta text-fg-muted">{message}</p>
    </div>
  );
}

/** Mini card inside the InfoWindow: title, count, when (spec §7). */
export function MapInfoCard({ event }: { event: EventListItem }) {
  return (
    <div className="flex w-52 flex-col gap-1 p-1 text-fg">
      <span className="flex items-center gap-1 text-meta text-fg-muted">
        <span aria-hidden>{event.sportIcon ?? "\u{1F4C5}"}</span>
        {formatTagLine(event.tags) || "Untagged"}
      </span>
      <span className="text-h3">{event.title}</span>
      <span className="text-meta text-fg-muted">
        <span data-count className="font-bold">
          {event.joinedCount}/{event.maxParticipants}
        </span>
        {" · "}
        {formatCardWhen(event)}
      </span>
      {/* Join lives in the card list; the InfoWindow keeps the map light and
          avoids a second optimistic writer on the same count. */}
      <span className="text-micro uppercase text-fg-muted">
        {formatTimeRange(event.startAt, event.endAt)}
      </span>
    </div>
  );
}
