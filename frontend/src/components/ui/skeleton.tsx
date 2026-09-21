import { cn } from "@/lib/cn";

/**
 * Skeletons reproduce the final geometry exactly — that is the entire CLS story
 * (spec §7/§11). `role="status"` + label keeps the wait announced once, not per block.
 */
export function SkeletonBlock({ className, ...rest }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      aria-hidden
      className={cn("animate-pulse rounded-sm bg-surface-2", className)}
      {...rest}
    />
  );
}

/** Exact EventCard geometry: sport row, title, meta, count + CTA footer. */
export function EventCardSkeleton() {
  return (
    <div className="rounded-md border border-border bg-surface p-4">
      <SkeletonBlock className="h-4 w-[120px]" />
      <SkeletonBlock className="mt-3 h-5 w-3/4" />
      <SkeletonBlock className="mt-2 h-3.5 w-7/12" />
      <div className="mt-4 flex items-center justify-between">
        <SkeletonBlock className="h-6 w-14" />
        <SkeletonBlock className="h-9 w-[88px] rounded-md" />
      </div>
    </div>
  );
}

/** Six of these on first paint of `/` (spec §8). */
export function EventCardListSkeleton({ count = 6 }: { count?: number }) {
  return (
    <div
      className="flex flex-col gap-4"
      role="status"
      aria-live="polite"
    >
      <span className="sr-only">Loading events</span>
      {Array.from({ length: count }, (_, i) => (
        <EventCardSkeleton key={i} />
      ))}
    </div>
  );
}

/** Grey block + label while the Maps JS API boots (spec §9). */
export function MapSkeleton({ label = "Loading map…" }: { label?: string }) {
  return (
    <div
      className="flex h-full min-h-48 w-full items-center justify-center bg-surface-2 text-meta text-fg-muted"
      role="status"
    >
      {label}
    </div>
  );
}
