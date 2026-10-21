"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import {
  DELETE_EVENT_CONFIRM,
  deleteCancelledEvent,
  refreshEventCaches,
  setEventStatus,
} from "@/features/events/host-actions";

export function HostingActions({
  eventId,
  isCancelled,
  isEnded,
  awaitingReview = false,
}: {
  eventId: string;
  isCancelled: boolean;
  isEnded: boolean;
  /** Waiting on an administrator's decision. Not a live event, so there is nothing
   * for the host to cancel: the submission is still in flight, and the review queue
   * is the only place it can be moved on from. */
  awaitingReview?: boolean;
}) {
  const [busy, setBusy] = useState<"cancel" | "reopen" | "delete" | null>(null);

  async function changeStatus(action: "cancel" | "reopen") {
    setBusy(action);
    try {
      await setEventStatus(eventId, action);
      await refreshEventCaches();
      toast(action === "cancel" ? "Event cancelled" : "Event reopened");
    } catch (error) {
      toast(error instanceof Error ? error.message : "Could not update this event", "info");
    } finally {
      setBusy(null);
    }
  }

  async function remove() {
    if (!window.confirm(DELETE_EVENT_CONFIRM)) return;
    setBusy("delete");
    try {
      await deleteCancelledEvent(eventId);
      await refreshEventCaches();
      toast("Event deleted", "success");
    } catch (error) {
      toast(error instanceof Error ? error.message : "Could not delete this event", "info");
      setBusy(null);
    }
  }

  // Ahead of the isEnded branch: a pending event is usually in the future, so
  // checking isEnded first would label it "Ended" and that is a lie.
  if (awaitingReview) {
    return (
      <Button size="sm" variant="secondary" disabled>
        Awaiting review
      </Button>
    );
  }

  // A cancelled event can be deleted whenever - including after its date, so old
  // cancellations can be cleared out of the list - and reopened while it is still
  // ahead.
  if (isCancelled) {
    return (
      <span className="inline-flex flex-wrap items-center gap-2">
        {!isEnded && (
          <Button
            size="sm"
            variant="primary"
            loading={busy === "reopen"}
            disabled={busy !== null}
            onClick={() => void changeStatus("reopen")}
          >
            Reopen
          </Button>
        )}
        <Button
          size="sm"
          variant="danger"
          loading={busy === "delete"}
          disabled={busy !== null}
          onClick={() => void remove()}
        >
          Delete
        </Button>
      </span>
    );
  }

  if (isEnded) {
    return (
      <Button size="sm" variant="secondary" disabled>
        Ended
      </Button>
    );
  }

  return (
    <Button size="sm" variant="danger" loading={busy === "cancel"} onClick={() => void changeStatus("cancel")}>
      Cancel event
    </Button>
  );
}
