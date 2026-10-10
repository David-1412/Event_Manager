"use client";

import { useState } from "react";
import { mutate as globalMutate } from "swr";
import { Dialog } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { VenuePicker, type VenueSelection } from "@/components/event/venue-picker";
import { toast } from "@/components/ui/toast";
import { detailKey, eventsKey, hostedKey } from "@/features/events/use-events";
import { ApiError, request } from "@/lib/api";
import type { EventDetail } from "@/types/events";

export function HostedEventEditor({
  eventId,
  canPublishPublicEvents,
  visibility,
}: {
  eventId: string;
  canPublishPublicEvents: boolean;
  visibility: "Public" | "Private";
}) {
  const [event, setEvent] = useState<EventDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [startAt, setStartAt] = useState("");
  const [endAt, setEndAt] = useState("");
  const [venue, setVenue] = useState<VenueSelection | null>(null);

  async function openEditor() {
    setLoading(true);
    try {
      const detail = await request<EventDetail>(`/api/events/${eventId}`);
      setEvent(detail);
      setTitle(detail.title);
      setDescription(detail.description ?? "");
      setStartAt(toLocalDateTime(detail.startAt));
      setEndAt(toLocalDateTime(detail.endAt));
      setVenue({
        venueName: detail.venueName,
        address: detail.address,
        latitude: detail.latitude,
        longitude: detail.longitude,
      });
    } catch (error) {
      toast(error instanceof ApiError ? error.detail ?? "Could not load this event." : "Could not load this event.");
    } finally {
      setLoading(false);
    }
  }

  async function save(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!event) return;
    setSaving(true);
    try {
      await request(`/api/events/${event.id}`, {
        method: "PUT",
        body: {
          title,
          description: description.trim() || null,
          venueName: venue?.venueName ?? event.venueName,
          address: venue?.address ?? event.address,
          thumbnailUrl: event.thumbnailUrl,
          latitude: venue?.latitude ?? event.latitude,
          longitude: venue?.longitude ?? event.longitude,
          timezone: event.timezone,
          startAt: new Date(startAt).toISOString(),
          endAt: new Date(endAt).toISOString(),
          maxParticipants: event.maxParticipants,
          skillLevel: event.skillLevel,
          cost: event.cost,
          status: event.status,
          visibility: event.visibility,
          tags: event.tags,
        },
      });
      await Promise.all([
        globalMutate(hostedKey()),
        globalMutate(detailKey(event.id)),
        globalMutate(eventsKey()),
      ]);
      toast(visibility === "Public" && !canPublishPublicEvents
        ? "Changes submitted for Admin approval."
        : "Event updated.");
      setEvent(null);
    } catch (error) {
      toast(error instanceof ApiError ? error.detail ?? "Could not update this event." : "Could not update this event.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <Button size="sm" variant="secondary" loading={loading} onClick={() => void openEditor()}>
        Edit event
      </Button>
      <Dialog open={event !== null} onClose={() => setEvent(null)} labelledBy={`hosted-edit-${eventId}`} className="max-h-[90vh] overflow-y-auto">
        <h2 id={`hosted-edit-${eventId}`} className="text-h3 text-fg">Edit event</h2>
        {visibility === "Public" && !canPublishPublicEvents && (
          <p className="mt-2 rounded-md border border-warn/40 bg-warn-tint px-3 py-2 text-meta text-fg">
            Public-event changes will be reviewed by an Admin before they appear in Browse.
          </p>
        )}
        <form className="mt-4 flex flex-col gap-3" onSubmit={(e) => void save(e)}>
          <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
            Title
            <input required minLength={3} maxLength={120} value={title} onChange={(e) => setTitle(e.currentTarget.value)}
              className="h-10 rounded-md border border-border bg-surface px-3 text-body text-fg" />
          </label>
          <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
            Description
            <textarea maxLength={2000} rows={4} value={description} onChange={(e) => setDescription(e.currentTarget.value)}
              className="rounded-md border border-border bg-surface px-3 py-2 text-body text-fg" />
          </label>
          <fieldset className="flex flex-col gap-2">
            <legend className="text-micro font-medium text-fg-muted">Location</legend>
            <VenuePicker value={venue} onChange={setVenue} />
          </fieldset>
          <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
            Start
            <input required type="datetime-local" value={startAt} onChange={(e) => setStartAt(e.currentTarget.value)}
              className="h-10 rounded-md border border-border bg-surface px-3 text-body text-fg" />
          </label>
          <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
            End
            <input required type="datetime-local" value={endAt} onChange={(e) => setEndAt(e.currentTarget.value)}
              className="h-10 rounded-md border border-border bg-surface px-3 text-body text-fg" />
          </label>
          <div className="mt-2 flex justify-end gap-2">
            <Button size="sm" variant="secondary" onClick={() => setEvent(null)}>Cancel</Button>
            <Button size="sm" type="submit" loading={saving}>Save changes</Button>
          </div>
        </form>
      </Dialog>
    </>
  );
}

function toLocalDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}
