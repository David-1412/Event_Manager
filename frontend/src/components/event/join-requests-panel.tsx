"use client";

import { useState } from "react";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { ApiError } from "@/lib/api";
import { formatDayMonth, formatTime } from "@/lib/format";
import { useJoinRequests } from "@/features/events/use-events";
import type { JoinRequest } from "@/types/events";

/**
 * The host's queue for one of their events: who has asked, and the two buttons that
 * answer them.
 *
 * Mounted only for an event the viewer hosts and that has approval turned on —
 * the endpoint is host-scoped and 404s anyone else, so fetching it outside those
 * conditions would show a failure where there is simply nothing to manage.
 *
 * Pending requests are the working list. Decided ones are reachable behind a toggle
 * rather than mixed in, because the queue's job is "what still needs a decision" and
 * a host scrolling past yesterday's rejections to find today's request fails at that.
 */
export function JoinRequestsPanel({ eventId }: { eventId: string }) {
  const [includeDecided, setIncludeDecided] = useState(false);
  const [busyUser, setBusyUser] = useState<string | null>(null);
  // A failed decision is stated here rather than in a toast: this component's toast
  // has no failure tone, and the request is still sitting in the list below the
  // message, so an inline line keeps the reason next to the thing it is about.
  const [decisionFailure, setDecisionFailure] = useState<string | null>(null);
  const { items, pendingCount, error, isLoading, approve, reject } = useJoinRequests(
    eventId,
    includeDecided,
  );

  async function decide(request: JoinRequest, action: "approve" | "reject") {
    const userId = request.requester.id;
    setBusyUser(userId);
    setDecisionFailure(null);
    try {
      if (action === "approve") await approve(userId);
      else await reject(userId);
      toast(
        action === "approve"
          ? `${request.requester.displayName} is in`
          : "Request declined",
        "success",
      );
    } catch (apiError) {
      // 409 EventFull is the one failure a host can act on: the queue is longer
      // than the event. Everything else is a generic retry, and the request stays
      // pending either way, so nothing is lost by the failure.
      setDecisionFailure(
        apiError instanceof ApiError && apiError.code === "EventFull"
          ? "This event is full — there is no spot left to approve into."
          : "Couldn’t record that decision — try again.",
      );
    } finally {
      setBusyUser(null);
    }
  }

  if (error) {
    // The endpoint is host-scoped, so a 404 here means either the event is gone or
    // this viewer is not the host. Both are "nothing for you to manage", and saying
    // that beats a stack trace over what is usually just a stale tab.
    return (
      <p className="text-meta text-fg-muted">
        Join requests could not be loaded right now.
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-h4">
          Join requests
          {pendingCount > 0 && (
            <Badge tone="warn" className="ml-2">
              {pendingCount} pending
            </Badge>
          )}
        </h2>
        <Button
          size="sm"
          variant="ghost"
          onClick={() => setIncludeDecided((current) => !current)}
        >
          {includeDecided ? "Pending only" : "Show decided"}
        </Button>
      </div>

      {decisionFailure && (
        <p role="alert" className="text-meta text-danger">
          {decisionFailure}
        </p>
      )}

      {isLoading ? (
        <p className="text-meta text-fg-muted">Loading…</p>
      ) : items.length === 0 ? (
        <p className="text-meta text-fg-muted">
          {includeDecided
            ? "No one has asked to join yet."
            : "Nothing waiting for a decision."}
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {items.map((request) => (
            <li
              key={request.requester.id}
              className="flex items-center gap-3 rounded-card border border-line bg-surface p-3"
            >
              <Avatar
                name={request.requester.displayName}
                src={request.requester.avatarUrl}
                size="sm"
              />
              <div className="min-w-0 flex-1">
                <p className="truncate text-body-strong">
                  {request.requester.displayName}
                </p>
                <p className="text-meta text-fg-muted">
                  Asked {formatDayMonth(request.requestedAt)} at{" "}
                  {formatTime(request.requestedAt)}
                  {request.status !== "Pending" && ` · ${request.status}`}
                </p>
              </div>
              {request.status === "Pending" ? (
                <div className="flex shrink-0 gap-2">
                  <Button
                    size="sm"
                    loading={busyUser === request.requester.id}
                    onClick={() => void decide(request, "approve")}
                  >
                    Approve
                  </Button>
                  <Button
                    size="sm"
                    variant="secondary"
                    disabled={busyUser === request.requester.id}
                    onClick={() => void decide(request, "reject")}
                  >
                    Reject
                  </Button>
                </div>
              ) : (
                <Badge tone={request.status === "Approved" ? "brand" : "neutral"}>
                  {request.status}
                </Badge>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
