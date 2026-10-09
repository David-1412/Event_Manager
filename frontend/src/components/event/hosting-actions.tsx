"use client";

import { useState } from "react";
import { mutate as globalMutate } from "swr";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { detailKey, eventsKey, hostedKey } from "@/features/events/use-events";
import { request } from "@/lib/api";

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
  const [isSaving, setIsSaving] = useState(false);

  async function changeStatus(action: "cancel" | "reopen") {
    setIsSaving(true);
    try {
      await request(`/api/events/${eventId}/${action}`, { method: "PATCH" });
      await Promise.all([
        globalMutate(hostedKey()),
        globalMutate(detailKey(eventId)),
        globalMutate(eventsKey()),
      ]);
      toast(action === "cancel" ? "Event cancelled" : "Event reopened");
    } catch (error) {
      toast(error instanceof Error ? error.message : "Could not update this event", "info");
    } finally {
      setIsSaving(false);
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

  if (isEnded) {
    return (
      <Button size="sm" variant="secondary" disabled>
        Ended
      </Button>
    );
  }

  return isCancelled ? (
    <Button size="sm" variant="primary" loading={isSaving} onClick={() => void changeStatus("reopen")}>
      Reopen
    </Button>
  ) : (
    <Button size="sm" variant="danger" loading={isSaving} onClick={() => void changeStatus("cancel")}>
      Cancel event
    </Button>
  );
}