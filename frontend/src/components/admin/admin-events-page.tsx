"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { Dialog } from "@/components/ui/dialog";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import { ApiError, usingFixtures } from "@/lib/api";
import { deleteAdminEvent, listAdminEvents, updateAdminEvent } from "@/lib/admin-events";
import type {
  AdminEvent,
  AdminEventTimeFrame,
  UpdateAdminEvent,
} from "@/types/admin-events";
import type { EventStatus } from "@/types/events";

const pageSize = 20;
const statuses: EventStatus[] = [
  "Scheduled", "Published", "Completed", "Cancelled", "Draft", "PendingReview", "Rejected",
];

export function AdminEventsPage() {
  const { user, loading } = useAuth();
  const [events, setEvents] = useState<AdminEvent[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [forbidden, setForbidden] = useState(false);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<EventStatus | "">("");
  const [timeFrame, setTimeFrame] = useState<AdminEventTimeFrame | null>(null);
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<AdminEvent | null>(null);
  const [deleting, setDeleting] = useState<AdminEvent | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (usingFixtures) {
      setEvents([]);
      setTotalCount(0);
      setIsLoading(false);
      return;
    }
    setIsLoading(true);
    try {
      const response = await listAdminEvents({
        q: search.trim(),
        timeFrame,
        status: status || null,
        page,
        pageSize,
      });
      setEvents(response.items);
      setTotalCount(response.totalCount);
      setForbidden(false);
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) setForbidden(true);
      else toast(error instanceof ApiError ? error.detail ?? error.message : "Could not load events.");
    } finally {
      setIsLoading(false);
    }
  }, [page, search, status, timeFrame]);

  useEffect(() => {
    if (loading) return;
    const timer = window.setTimeout(() => void load(), search ? 250 : 0);
    return () => window.clearTimeout(timer);
  }, [loading, load, search]);

  function changeTimeFrame(frame: AdminEventTimeFrame | null) {
    setTimeFrame(frame);
    setPage(1);
  }

  async function saveEvent(event: UpdateAdminEvent) {
    if (!editing) return;
    setBusy(true);
    try {
      await updateAdminEvent(editing.id, event);
      toast("Event updated.");
      setEditing(null);
      await load();
    } catch (error) {
      toast(error instanceof ApiError ? error.detail ?? "Could not update event." : "Could not update event.");
    } finally {
      setBusy(false);
    }
  }

  async function confirmDelete() {
    if (!deleting) return;
    setBusy(true);
    try {
      await deleteAdminEvent(deleting.id);
      toast("Event removed from the site. It is retained in the database.");
      setDeleting(null);
      if (events.length === 1 && page > 1) setPage(page - 1);
      else await load();
    } catch (error) {
      toast(error instanceof ApiError ? error.detail ?? "Could not delete event." : "Could not delete event.");
    } finally {
      setBusy(false);
    }
  }

  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));
  const timeTabs = useMemo(() => [
    { label: "All", value: null },
    { label: "Upcoming", value: "Future" as const },
    { label: "Ongoing", value: "Current" as const },
    { label: "Past", value: "Past" as const },
  ], []);

  if (loading) {
    return (
      <main className="mx-auto flex w-full max-w-[1200px] flex-col gap-4 px-4 py-6">
        <h1 className="text-h1 text-fg">Manage events</h1>
        <div className="h-48 animate-pulse rounded-lg bg-surface-2" aria-busy="true" />
      </main>
    );
  }

  if (!user) {
    return (
      <main className="mx-auto w-full max-w-[1200px] px-4 py-6">
        <h1 className="text-h1 text-fg">Manage events</h1>
        <p className="mt-4 rounded-md border border-dashed border-border px-4 py-8 text-center text-meta text-fg-muted">
          Sign in with an Admin account.{" "}
          <Link href="/login?next=%2Fadmin%2Fevents" className="text-brand-600 underline">Sign in</Link>
        </p>
      </main>
    );
  }

  if (forbidden) {
    return (
      <main className="mx-auto w-full max-w-[1200px] px-4 py-6">
        <h1 className="text-h1 text-fg">Not found</h1>
        <p className="mt-4 text-meta text-fg-muted">
          This page doesn&rsquo;t exist for your account. <Link href="/" className="text-brand-600 underline">Back to browse</Link>
        </p>
      </main>
    );
  }

  return (
    <main className="mx-auto flex w-full max-w-[1200px] flex-col gap-5 px-4 py-6">
      <div className="flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <h1 className="text-h1 text-fg">Manage events</h1>
          <p className="mt-1 text-meta text-fg-muted">Edit event details and status, or remove an event from the site.</p>
        </div>
        <span className="text-meta text-fg-muted">{totalCount} event{totalCount === 1 ? "" : "s"}</span>
      </div>

      <div className="flex flex-col gap-4 rounded-lg border border-border bg-surface p-4">
        <div className="flex flex-wrap gap-2" role="tablist" aria-label="Filter events by time">
          {timeTabs.map((tab) => {
            const selected = timeFrame === tab.value;
            return (
              <button key={tab.label} type="button" role="tab" aria-selected={selected}
                onClick={() => changeTimeFrame(tab.value)}
                className={`rounded-md px-4 py-2 text-meta font-medium ${selected ? "bg-brand-600 text-white" : "border border-border text-fg hover:bg-surface-2"}`}>
                {tab.label}
              </button>
            );
          })}
        </div>
        <div className="grid gap-3 sm:grid-cols-[minmax(12rem,1fr)_14rem]">
          <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
            Search
            <input value={search} onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }}
              placeholder="Title, host, or venue"
              className="h-10 rounded-md border border-border bg-surface px-3 text-body text-fg placeholder:text-fg-muted" />
          </label>
          <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
            Status
            <select value={status} onChange={(e) => { setStatus(e.currentTarget.value as EventStatus | ""); setPage(1); }}
              className="h-10 rounded-md border border-border bg-surface px-3 text-body text-fg">
              <option value="">All statuses</option>
              {statuses.map((item) => <option key={item} value={item}>{item}</option>)}
            </select>
          </label>
        </div>
      </div>

      {isLoading && events.length === 0 ? (
        <div className="h-40 animate-pulse rounded-lg bg-surface-2" aria-label="Loading events" />
      ) : events.length === 0 ? (
        <p className="rounded-md border border-dashed border-border px-4 py-10 text-center text-meta text-fg-muted">
          {usingFixtures ? "Event management is available when connected to the API." : "No matching events."}
        </p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-border" aria-busy={isLoading}>
          <table className="w-full border-collapse text-left text-meta">
            <thead><tr className="border-b border-border bg-surface-2 text-micro uppercase text-fg-muted">
              <th scope="col" className="px-3 py-3 font-medium">Title</th>
              <th scope="col" className="px-3 py-3 font-medium">Host</th>
              <th scope="col" className="px-3 py-3 font-medium">Start date</th>
              <th scope="col" className="px-3 py-3 font-medium">End date</th>
              <th scope="col" className="px-3 py-3 font-medium">Status</th>
              <th scope="col" className="px-3 py-3 text-right font-medium">Actions</th>
            </tr></thead>
            <tbody>
              {events.map((event) => (
                <tr key={event.id} className="border-b border-border last:border-0">
                  <td className="max-w-56 px-3 py-3 text-fg">
                    <span className="block truncate font-medium">{event.title}</span>
                    <span className="block truncate text-micro text-fg-muted">{event.venueName}</span>
                  </td>
                  <td className="px-3 py-3 text-fg-muted">{event.hostName}</td>
                  <td className="whitespace-nowrap px-3 py-3 text-fg-muted">{formatDate(event.startAt)}</td>
                  <td className="whitespace-nowrap px-3 py-3 text-fg-muted">{formatDate(event.endAt)}</td>
                  <td className="px-3 py-3"><StatusBadge status={event.status} /></td>
                  <td className="whitespace-nowrap px-3 py-3 text-right">
                    <button type="button" onClick={() => setEditing(event)}
                      className="rounded px-2 py-1 text-brand-600 hover:bg-brand-tint">Edit</button>
                    <button type="button" onClick={() => setDeleting(event)}
                      className="rounded px-2 py-1 text-danger hover:bg-danger/10">Delete</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="flex items-center justify-between gap-3 text-meta text-fg-muted">
        <span>Page {page} of {pageCount}</span>
        <div className="flex gap-2">
          <button type="button" disabled={page <= 1} onClick={() => setPage((value) => value - 1)}
            className="h-9 rounded-md border border-border px-3 disabled:opacity-40">Previous</button>
          <button type="button" disabled={page >= pageCount} onClick={() => setPage((value) => value + 1)}
            className="h-9 rounded-md border border-border px-3 disabled:opacity-40">Next</button>
        </div>
      </div>

      <EditEventDialog key={editing?.id ?? "closed"} event={editing} busy={busy} onClose={() => setEditing(null)} onSave={(value) => void saveEvent(value)} />
      <Dialog open={deleting !== null} onClose={() => setDeleting(null)} labelledBy="admin-event-delete-title">
        <h2 id="admin-event-delete-title" className="text-h3 text-fg">Remove event?</h2>
        <p className="mt-3 text-body text-fg-muted">
          {deleting ? <><strong>{deleting.title}</strong> will be hidden from the site. Its record is retained in the database.</> : null}
        </p>
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" onClick={() => setDeleting(null)} className="h-10 rounded-md border border-border px-4 text-meta">Cancel</button>
          <button type="button" disabled={busy} onClick={() => void confirmDelete()} className="h-10 rounded-md bg-danger px-4 text-meta font-medium text-white disabled:opacity-50">
            {busy ? "Removing…" : "Remove event"}
          </button>
        </div>
      </Dialog>
    </main>
  );
}

function EditEventDialog({ event, busy, onClose, onSave }: {
  event: AdminEvent | null;
  busy: boolean;
  onClose: () => void;
  onSave: (value: UpdateAdminEvent) => void;
}) {
  const [title, setTitle] = useState(event?.title ?? "");
  const [description, setDescription] = useState(event?.description ?? "");
  const [startAt, setStartAt] = useState(event ? toLocalDateTime(event.startAt) : "");
  const [endAt, setEndAt] = useState(event ? toLocalDateTime(event.endAt) : "");
  const [status, setStatus] = useState<EventStatus>(event?.status ?? "Scheduled");

  function submit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!event) return;
    onSave({
      title,
      description: description.trim() || null,
      venueName: event.venueName,
      address: event.address,
      thumbnailUrl: event.thumbnailUrl,
      latitude: event.latitude,
      longitude: event.longitude,
      timezone: event.timezone,
      startAt: new Date(startAt).toISOString(),
      endAt: new Date(endAt).toISOString(),
      maxParticipants: event.maxParticipants,
      skillLevel: event.skillLevel,
      cost: event.cost,
      status,
      visibility: event.visibility,
      tags: event.tags,
    });
  }

  return (
    <Dialog open={event !== null} onClose={onClose} labelledBy="admin-event-edit-title" className="max-h-[90vh] overflow-y-auto">
      <h2 id="admin-event-edit-title" className="text-h3 text-fg">Edit event</h2>
      <form className="mt-4 flex flex-col gap-3" onSubmit={submit}>
        <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
          Title
          <input required maxLength={120} value={title} onChange={(e) => setTitle(e.currentTarget.value)}
            className="h-10 rounded-md border border-border bg-surface px-3 text-body text-fg" />
        </label>
        <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
          Description
          <textarea maxLength={2000} rows={4} value={description} onChange={(e) => setDescription(e.currentTarget.value)}
            className="rounded-md border border-border bg-surface px-3 py-2 text-body text-fg" />
        </label>
        <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
          Start date and time
          <input required type="datetime-local" value={startAt} onChange={(e) => setStartAt(e.currentTarget.value)}
            className="h-10 rounded-md border border-border bg-surface px-3 text-body text-fg" />
        </label>
        <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
          End date and time
          <input required type="datetime-local" value={endAt} onChange={(e) => setEndAt(e.currentTarget.value)}
            className="h-10 rounded-md border border-border bg-surface px-3 text-body text-fg" />
        </label>
        <label className="flex flex-col gap-1 text-micro font-medium text-fg-muted">
          Status
          <select value={status} onChange={(e) => setStatus(e.currentTarget.value as EventStatus)}
            className="h-10 rounded-md border border-border bg-surface px-3 text-body text-fg">
            {statuses.map((item) => <option key={item} value={item}>{item}</option>)}
          </select>
        </label>
        <div className="mt-2 flex justify-end gap-2">
          <button type="button" onClick={onClose} className="h-10 rounded-md border border-border px-4 text-meta">Cancel</button>
          <button type="submit" disabled={busy} className="h-10 rounded-md bg-brand-600 px-4 text-meta font-medium text-white disabled:opacity-50">
            {busy ? "Saving…" : "Save changes"}
          </button>
        </div>
      </form>
    </Dialog>
  );
}

function StatusBadge({ status }: { status: EventStatus }) {
  const classes = status === "Completed"
    ? "bg-brand-tint text-brand-700"
    : status === "Cancelled" || status === "Rejected"
      ? "bg-danger/10 text-danger"
      : status === "PendingReview"
        ? "bg-brand-tint text-brand-600"
        : "bg-surface-2 text-fg";
  return <span className={`inline-flex rounded-full px-2 py-1 text-micro font-medium ${classes}`}>{status}</span>;
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "—" : new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
}

function toLocalDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}
