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
}: {
  eventId: string;
  isCancelled: boolean;
  isEnded: boolean;
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