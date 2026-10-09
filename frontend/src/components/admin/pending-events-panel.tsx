"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/dialog";
import { toast } from "@/components/ui/toast";
import { ApiError, usingFixtures } from "@/lib/api";
import { approveEvent, listPendingEvents, rejectEvent } from "@/lib/admin";
import { formatDayMonth, formatTime } from "@/lib/format";
import type { PendingEvent } from "@/types/admin";

/**
 * The queue of public events regular users have submitted and that are waiting for
 * a decision — requirement 11, and the reason an Admin exists in this product.
 *
 * Reads `GET /api/events/reviews/pending`, which builds its rows from the same
 * `v_event_feed` the browse cards come from, so a queue row is the card its creator
 * will see if it is approved. Approve publishes immediately; reject returns the
 * event to its creator, who can edit and resubmit it.
 *
 * Both decisions are confirmed first. Publishing something under someone else's name
 * is not an action to fire on an accidental click.
 */
export function PendingEventsPanel() {
  const [events, setEvents] = useState<PendingEvent[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [acting, setActing] = useState<{ event: PendingEvent; decision: "approve" | "reject" } | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (usingFixtures) {
      setIsLoading(false);
      return;
    }
    setIsLoading(true);
    try {
      setEvents(await listPendingEvents());
      setFailed(false);
    } catch {
      // A Member reaches this panel only by editing the URL, and the endpoint 404s
      // them. Showing "nothing to review" would be the lie; showing a failure that
      // is really a permission answer is honest without confirming anything.
      setFailed(true);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  async function confirmDecision() {
    if (!acting) return;
    setBusy(true);
    try {
      if (acting.decision === "approve") await approveEvent(acting.event.id);
      else await rejectEvent(acting.event.id);
      setEvents((rows) => rows.filter((r) => r.id !== acting.event.id));
      toast(acting.decision === "approve" ? "Event published" : "Event rejected");
    } catch (err) {
      toast(err instanceof ApiError ? err.detail ?? "Could not record the decision." : "Could not record the decision.");
      void load();
    } finally {
      setBusy(false);
      setActing(null);
    }
  }


  if (isLoading) {
    return (
      <section aria-labelledby="admin-pending-title" className="flex flex-col gap-3">
        <h2 id="admin-pending-title" className="text-h2 text-fg">Awaiting approval</h2>
        <ul className="flex flex-col gap-2" aria-busy="true">
          {[0, 1].map((i) => (
            <li key={i} className="h-16 animate-pulse rounded-md bg-surface-2" />
          ))}
        </ul>
      </section>
    );
  }

  if (failed) {
    return (
      <section aria-labelledby="admin-pending-title" className="flex flex-col gap-3">
        <h2 id="admin-pending-title" className="text-h2 text-fg">Awaiting approval</h2>
        <p className="rounded-md border border-dashed border-border px-4 py-6 text-center text-meta text-fg-muted">
          The review queue could not be loaded.
        </p>
      </section>
    );
  }

  return (
    <section aria-labelledby="admin-pending-title" className="flex flex-col gap-3">
      <div className="flex items-baseline justify-between gap-3">
        <h2 id="admin-pending-title" className="text-h2 text-fg">Awaiting approval</h2>
        {events.length > 0 && (
          <span className="text-meta text-fg-muted">
            {events.length} event{events.length === 1 ? "" : "s"}
          </span>
        )}
      </div>

      {events.length === 0 ? (
        <p className="rounded-md border border-dashed border-border px-4 py-6 text-center text-meta text-fg-muted">
          Nothing to review. Public events submitted by regular users land here before
          they reach the feed.
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {events.map((event) => (
            <li
              key={event.id}
              className="flex flex-wrap items-center gap-3 rounded-md border border-border bg-surface p-3"
            >
              <div className="min-w-0 flex-1">
                <Link
                  href={`/events/${event.id}`}
                  className="block truncate text-body font-medium text-fg hover:underline"
                >
                  {event.title}
                </Link>
                <p className="truncate text-micro text-fg-muted">
                  {event.hostName ? `${event.hostName} · ` : ""}
                  {formatDayMonth(event.startAt)} {formatTime(event.startAt)}
                  {event.venueName ? ` · ${event.venueName}` : ""}
                  {` · ${event.maxParticipants} spots`}
                </p>
              </div>
              <div className="flex shrink-0 items-center gap-2">
                <Button variant="ghost" size="sm" onClick={() => setActing({ event, decision: "reject" })}>
                  Reject
                </Button>
                <Button size="sm" onClick={() => setActing({ event, decision: "approve" })}>
                  Approve
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <ConfirmDialog
        open={acting !== null}
        titleId="admin-event-decision-title"
        title={acting?.decision === "approve" ? "Publish event" : "Reject event"}
        body={
          acting ? (
            <span>
              {acting.decision === "approve" ? (
                <>
                  {"Publish "}
                  <strong>{acting.event.title}</strong>
                  {" to the public feed? It goes live immediately under "}
                  <strong>{acting.event.hostName ?? "its host"}</strong>
                  {"&rsquo;s name."}
                </>
              ) : (
                <>
                  {"Reject "}
                  <strong>{acting.event.title}</strong>
                  {"? It stays out of the feed. "}
                  <strong>{acting.event.hostName ?? "Its host"}</strong>
                  {" can edit and resubmit it."}
                </>
              )}
            </span>
          ) : null
        }
        confirmLabel={acting?.decision === "approve" ? "Publish" : "Reject"}
        busy={busy}
        onConfirm={() => void confirmDecision()}
        onClose={() => setActing(null)}
      />
    </section>
  );
}
