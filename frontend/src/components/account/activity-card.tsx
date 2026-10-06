"use client";

import Link from "next/link";
import { Card } from "@/components/ui/card";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { useUserStats } from "@/features/account/use-account";
import { cn } from "@/lib/cn";
import type { UserStats } from "@/types/account";
import { CardError } from "./card-error";

const TILES: { key: keyof UserStats; label: string }[] = [
  { key: "eventsCreated", label: "Events created" },
  { key: "interestedEvents", label: "Interested" },
  { key: "upcomingEvents", label: "Upcoming" },
];

/** `Button` renders a <button>; these are navigations, so the same look on a Link. */
const linkBase =
  "press inline-flex h-11 items-center justify-center rounded-md px-5 text-body font-medium";

export function ActivityCard() {
  const { stats, error, isLoading, retry } = useUserStats();

  return (
    <Card className="p-4 sm:p-6">
      <h2 className="text-h3 text-fg">Activity</h2>
      <div className="mt-4">
        {error ? (
          <CardError message="We couldn't load your activity." onRetry={retry} />
        ) : (
          <dl className="grid grid-cols-3 gap-3" aria-busy={isLoading || !stats}>
            {TILES.map(({ key, label }) => (
              <div key={key} className="rounded-md border border-border bg-surface-2 p-3 sm:p-4">
                <dt className="text-meta text-fg-muted">{label}</dt>
                <dd className="mt-1 text-h2 text-fg">
                  {stats ? stats[key] : <SkeletonBlock className="h-7 w-10" />}
                </dd>
              </div>
            ))}
          </dl>
        )}
      </div>
      <div className="mt-4 flex flex-col gap-2 sm:flex-row">
        <Link
          href="/my-events"
          className={cn(linkBase, "border border-border bg-surface text-fg hover:bg-surface-2")}
        >
          View my events
        </Link>
        <Link href="/create" className={cn(linkBase, "bg-brand-600 text-brand-fg hover:bg-brand-700")}>
          Create event
        </Link>
      </div>
    </Card>
  );
}
