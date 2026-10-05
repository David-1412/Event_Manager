"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";


import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { EventCard } from "@/components/event/event-card";
import { type VenueSelection } from "@/components/event/venue-picker";
import { toast } from "@/components/ui/toast";
import { ApiError, request, usingFixtures } from "@/lib/api";
import { useAuth } from "@/lib/auth/auth-provider";
import { updateDraft, createManualDraft, getDraft, approveDraft } from "@/lib/drafts";
import { refreshDraftQueue } from "@/features/create/use-drafts";
import { payloadToFormValues, payloadToVenue } from "@/features/create/use-draft-to-form";
import {
  clearLocalDraftEdit,
  loadLocalDraftEdit,
  parseLocalDraftEdit,
  saveLocalDraftEdit,
} from "@/features/create/draft-autosave";
import { DraftsPastePanel } from "@/components/event/drafts-paste-panel";
import { DraftsSection, DraftBanner } from "@/components/event/drafts-section";
import { ConfirmDialog } from "@/components/ui/dialog";


import { signInReturnTo } from "@/lib/auth/types";
import {
  CREATE_EVENT_DEFAULTS,
  createEventSchema,
  toCreateEventPayload,
  type CreateEventValues,
} from "@/features/create/create-event-schema";
import { melbourneDateInputValue } from "@/lib/format";
import { normalizeTags } from "@/lib/sports";
import { fixtureAddDraft, fixtureSaveDraft } from "@/lib/fixtures";
import { CreateEventGroups } from "@/components/event/create-event-groups";
import { CreateEventFooter } from "@/components/event/create-event-footer";
import { previewOf } from "@/components/event/create-event-preview";
import type { DraftPayload, EventDraft, ExtractNowResponse } from "@/types/events";

/**
 * `/create` (spec §8): five groups - What / When / Where / Who and how much /
 * Description - with a live preview card pinned top-right at `lg+`. Errors show
 * on blur or submit, never mid-first-word; a server `422` lands on the matching
 * `Field`; the summary at the top makes the failure unmistakable.
 */
export function CreateEventView() {
  const router = useRouter();
  const { user, loading } = useAuth();
  const uid = user?.uid ?? null;

  const [venue, setVenue] = useState<VenueSelection | null>(null);
  const [serverErrors, setServerErrors] = useState<Record<string, string>>({});
  const [submitFailed, setSubmitFailed] = useState(false);

  const form = useForm<CreateEventValues>({
    resolver: zodResolver(createEventSchema),
    defaultValues: { ...CREATE_EVENT_DEFAULTS, date: melbourneDateInputValue() },
    mode: "onBlur",
  });
  // `form.watch()` is the documented way to drive a live preview. React Compiler
  // skips memoizing anything touching it (it hands back a function), which is a
  // performance advisory rather than a defect - `/create` is one screen and the
  // preview re-render is the whole point. `useWatch` was tried and widened every
  // field to optional, pushing `undefined` handling into the preview and groups.
   
  const values = form.watch();

  // Draft review wiring. A draft opened via ?draftId= populates the form; the
  // reviewer edits it, autosaves it back (localStorage on every change, the API
  // on exit/manual save) and publishes it through approve — the authenticated
  // user becomes the event's host. Missing fields and confidence are review
  // guidance, not blockers, so the form is always editable and saveable.
  const [draftId, setDraftId] = useState<string | null>(null);
  const [draftMissing, setDraftMissing] = useState<string[]>([]);
  const [draftConfidence, setDraftConfidence] = useState<number | null>(null);
  const [isDirty, setIsDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [promptExit, setPromptExit] = useState(false);
  const exitPathRef = useRef<string | null>(null);
  const publishedRef = useRef(false);
  const draftRef = useRef<string | null>(null);
  const hydratedRef = useRef(false);
  const latestRef = useRef<{ values: CreateEventValues; venue: VenueSelection | null }>({
    values,
    venue: null,
  });


  // `/create` is listed as auth-required (plan §13). Firebase never put the
  // session in a cookie, so middleware can't see it and the guard has to live
  // here — but it waits for `loading`, or a session that is still restoring
  // would be bounced to the login page on a hard refresh.
  useEffect(() => {
    if (!loading && !user) router.replace(signInReturnTo("/create"));
  }, [loading, user, router]);

  const errorFor = (name: keyof CreateEventValues) =>
    form.formState.errors[name]?.message ?? serverErrors[name];

  function applyDraftToForm(draft: EventDraft) {
    const serverValues = payloadToFormValues(draft.payload);
    const edit = loadLocalDraftEdit(draft.id);
    const local = parseLocalDraftEdit<CreateEventValues>(edit);
    const serverTouched = draft.reviewedAt ?? draft.createdAt;
    const useLocal = local != null && edit != null && (!serverTouched || edit.savedAt > serverTouched);
    const chosen = useLocal ? { ...serverValues, ...local } : serverValues;
    const nextVenue = payloadToVenue(draft.payload);
    const nextValues = { ...chosen, date: chosen.date || melbourneDateInputValue() };

    draftRef.current = draft.id;
    setDraftId(draft.id);
    form.reset(nextValues);
    setVenue(nextVenue);
    latestRef.current = { values: nextValues, venue: nextVenue };
    setDraftMissing(draft.missingFields);
    setDraftConfidence(draft.confidence);
    setServerErrors({});
    setSubmitFailed(false);
    setIsDirty(false);
    publishedRef.current = false;
    hydratedRef.current = true;
    if (useLocal) toast("Restored your unsaved edits for this draft");
  }

  function selectPendingDraft(draft: EventDraft) {
    applyDraftToForm(draft);
    router.push(`/create?draftId=${draft.id}`);
  }

  // Open a draft for review (?draftId=): pull it, map its payload into the form,
  // and start clean (populating is not a user edit). A newer local autosave wins
  // over the server copy — it is the edit this browser made after the last PUT,
  // and silently discarding it on revisit is the failure autosave exists for.
  useEffect(() => {
    const id = new URLSearchParams(window.location.search).get("draftId");
    if (!id) return;
    let active = true;
    void getDraft(id)
      .then((draft) => {
        if (!active) return;
        applyDraftToForm(draft);
      })
      .catch((error: unknown) => {
        if (!active) return;
        const message =
          error instanceof ApiError && error.status === 401
            ? "Sign in to review this draft."
            : "Could not load that draft.";
        toast(message);
      });

    return () => {
      active = false;
    };
    // form/reset is stable for the life of the view; run once for the URL's draftId.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Track edits for the exit prompt and autosave. Every keystroke writes the
  // localStorage copy (cheap, synchronous, survives a dead tab); the API save
  // happens on the in-app Exit dialog's "Save and exit".
  useEffect(() => {
    // React Compiler cannot memoize react-hook-form's watch() callback; the
    // directive sits here, on the callback, exactly where the rule fires.
    // eslint-disable-next-line react-hooks/incompatible-library
    const sub = form.watch((next) => {
      latestRef.current = { values: next as CreateEventValues, venue: latestRef.current.venue };
      setIsDirty(true);
      if (draftRef.current && hydratedRef.current) {
        saveLocalDraftEdit(draftRef.current, next);
      }
    });
    return () => sub.unsubscribe();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Forceful exit (tab closed / reload): the browser can only show a native
  // "Leave site?" confirmation — it cannot run an async write. The autosaved
  // localStorage copy covers the data; this makes the hard-unload visible so the
  // reviewer is never silently dropped without a chance to keep their edits.
  useEffect(() => {
    if (!isDirty) return;
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [isDirty]);

  async function persistDraft(): Promise<boolean> {
    setSaving(true);
    try {
      const payload = buildDraftPayload(latestRef.current.values, latestRef.current.venue);
      if (usingFixtures) {
        const saved = draftId
          ? fixtureSaveDraft(draftId, payload)
          : fixtureAddDraft(payload);
        if (!saved) throw new Error("Could not save this draft");
        setDraftId(saved.id);
        setDraftConfidence(saved.confidence);
        setDraftMissing(saved.missingFields);
      } else if (draftId) {
        await updateDraft(draftId, payload);
      } else {
        const saved = await createManualDraft(
          payload,
          latestRef.current.values.title || "Manual event draft",
          latestRef.current.values.description || "Manual event draft",
        );
        setDraftId(saved.id);
        setDraftConfidence(saved.confidence);
        setDraftMissing(saved.missingFields);
      }
      if (draftId) clearLocalDraftEdit(draftId);
      setIsDirty(false);
      refreshDraftQueue(uid);
      return true;
    } catch {
      toast("Could not save the draft");
      return false;
    } finally {
      setSaving(false);
    }
  }

  // Manual exit: save (if editing a draft) then navigate, or prompt if unsaved
  // changes. The local autosave already holds the bytes; this is the chance to
  // push them to the server so they survive a different device.
  function attemptExit(path: string) {
    if (!draftId || !isDirty) {
      router.push(path);
      return;
    }
    exitPathRef.current = path;
    setPromptExit(true);
  }



  function applyVenue(next: VenueSelection | null) {
    setVenue(next);
    latestRef.current = { values: latestRef.current.values, venue: next };
    form.setValue("venueName", next?.venueName ?? "", { shouldValidate: true });
    form.setValue("address", next?.address ?? "");
    form.setValue("latitude", next?.latitude ?? 0);
    form.setValue("longitude", next?.longitude ?? 0);
  }

  function applyExtractedDraft(result: ExtractNowResponse) {
    if (!result.payload) return;
    const nextValues = payloadToFormValues(result.payload);
    const nextVenue = payloadToVenue(result.payload);
    form.reset(nextValues);
    setVenue(nextVenue);
    latestRef.current = { values: nextValues, venue: nextVenue };
    setDraftId(result.draftId);
    setDraftMissing(result.missingFields ?? []);
    setDraftConfidence(result.confidence);
    setServerErrors({});
    setSubmitFailed(false);
    setIsDirty(false);
  }


  async function onSubmit(draft: CreateEventValues) {
    setSubmitFailed(false);
    setServerErrors({});
    const parsed = createEventSchema.safeParse(draft);
    if (!parsed.success) {
      parsed.error.issues.forEach((issue) =>
        form.setError(String(issue.path[0]) as keyof CreateEventValues, {
          message: issue.message,
        }),
      );
      focusFirstError(parsed.error.issues.map((i) => String(i.path[0])));
      setSubmitFailed(true);
      return;
    }
    if (!venue) {
      form.setError("venueName", { message: "Search for a venue" });
      focusFirstError(["venueName"]);
      setSubmitFailed(true);
      return;
    }
    const payload = toCreateEventPayload({ ...parsed.data, ...venue });
    try {
      // A draft publishes through approve — the draft closes, the approving user
      // becomes the host, and it leaves the queue; a plain create posts normally.
      const created =
        draftId && !usingFixtures
          ? await approveDraft(draftId, payload)
          : await postEvent(payload);
      // The event is published; navigating away is no longer an unsaved-draft exit.
      publishedRef.current = true;
      if (draftId) {
        clearLocalDraftEdit(draftId);
        refreshDraftQueue(uid);
      }
      setIsDirty(false);
      toast("Event created", "success");

      router.push(`/events/${created.id}`);
    } catch (error) {
      if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
        const mapped: Record<string, string> = {};
        Object.entries(error.errors).forEach(([key, messages]) => {
          mapped[toCamel(key)] = messages[0] ?? "Check this value";
        });
        setServerErrors(mapped);
        Object.entries(mapped).forEach(([key, message]) =>
          form.setError(key as keyof CreateEventValues, { message }),
        );
        focusFirstError(Object.keys(mapped));
      }
      setSubmitFailed(true);
    }
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="mx-auto flex w-full max-w-[1000px] flex-col gap-8 px-4 pb-28 pt-6 lg:grid lg:grid-cols-[minmax(0,1fr)_340px] lg:gap-10 lg:pb-10"
    >
      <div className="flex min-w-0 flex-col gap-8">
        <h1 className="text-h1 text-fg">Create an event</h1>
        {submitFailed && (
          <p
            role="alert"
            className="rounded-md border border-danger bg-surface p-3 text-meta text-danger"
          >
            Some fields need attention - see the highlights below.
          </p>
        )}
        {draftId && (
          <DraftBanner title={values.title} confidence={draftConfidence} missingFields={draftMissing} />
        )}
        <DraftsPastePanel onExtracted={applyExtractedDraft} />
        <CreateEventGroups

          values={values}
          venue={venue}
          errorFor={errorFor}
          register={form.register}
          onTags={(tags) => form.setValue("tags", tags, { shouldValidate: true })}
          onVenue={applyVenue}
        />
      </div>

      {/* Live preview: the cheapest way to teach what the listing will look like. */}
      <aside className="hidden lg:block">
        <div className="sticky top-20 flex flex-col gap-2">
          <p className="text-meta text-fg-muted">Live preview</p>
          <EventCard event={previewOf(values, venue)} />
        </div>
      </aside>

      <CreateEventFooter
        submitting={form.formState.isSubmitting}
        submittingLabel={draftId ? "Publish draft" : "Create event"}
        onCancel={draftId ? () => attemptExit("/create") : undefined}
        onSave={() => void persistDraft().then((ok) => ok && toast("Draft saved", "success"))}
        saveBusy={saving}
      />

      {/* The queue lives under the form: review happens here, and a reviewer
          finishing one draft should see the next without leaving the page. */}
      <div className="lg:col-span-2 flex flex-col gap-4 border-t border-border pt-6">
        <DraftsSection onOpenDraft={selectPendingDraft} />
      </div>


      <ConfirmDialog
        open={promptExit}
        titleId="draft-exit-title"
        title="Save changes to this draft?"
        body={<span>You have unsaved edits. Save them to this draft before leaving?</span>}
        confirmLabel="Save and exit"
        busy={saving}
        onConfirm={async () => {
          const path = exitPathRef.current ?? "/";
          setPromptExit(false);
          const ok = await persistDraft();
          if (ok) router.push(path);
        }}
        onClose={() => {
          setPromptExit(false);
          exitPathRef.current = null;
        }}
      />

    </form>
  );
}

/** Focus the first invalid control so keyboard users are never stranded. */
function focusFirstError(names: string[]) {
  for (const name of names) {
    const el = document.querySelector<HTMLElement>(`[name="${name}"]`);
    if (el) {
      el.scrollIntoView({ block: "center", behavior: "smooth" });
      el.focus({ preventScroll: true });
      return;
    }
  }
}

/** `MaxParticipants` / `max_participants` -> `maxParticipants`. */
function toCamel(key: string): string {
  return key
    .replace(/^_+/, "")
    .toLowerCase()
    .replace(/_([a-z])/g, (_, letter: string) => letter.toUpperCase());
}

async function postEvent(
  payload: ReturnType<typeof toCreateEventPayload>,
): Promise<{ id: string }> {
  if (usingFixtures) {
    // Offline we hand back a fixture the detail page can actually resolve,
    // rather than pretending a write happened.
    return { id: "evt-badminton-monday" };
  }
  return request<{ id: string }>("/api/events", { method: "POST", body: payload });
}

/**
 * Build the autosave payload from the form's *current* values — deliberately
 * not `toCreateEventPayload`, which requires a schema-valid form: autosave runs
 * on half-filled forms by design, and the whole draft contract is that gaps are
 * legal. Each field converts independently so one unparsable input (a cleared
 * date, say) drops that field rather than the whole save, and the server's
 * missing-field recomputation reports it again on the next load.
 */
function buildDraftPayload(
  v: CreateEventValues,
  venue: VenueSelection | null,
): DraftPayload {
  const start = parseWallClock(v.date, v.startTime);
  const end = parseWallClock(v.date, v.endTime);
  const spots = Number(v.maxParticipants);
  const cost = Number(v.cost);

  return {
    title: v.title?.trim() || "",
    tags: normalizeTags(v.tags ?? []),
    ...(start ? { startAt: start.toISOString() } : {}),
    ...(end ? { endAt: end.toISOString() } : {}),
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || "Australia/Melbourne",
    venueName: venue?.venueName ?? v.venueName ?? "",
    address: venue?.address ?? v.address ?? "",
    ...(venue ? { latitude: venue.latitude, longitude: venue.longitude } : {}),
    ...(Number.isFinite(spots) && spots >= 2 && spots <= 50 ? { maxParticipants: Math.trunc(spots) } : {}),
    ...(v.cost?.trim() && Number.isFinite(cost) ? { cost } : {}),
    ...(v.description?.trim() ? { description: v.description.trim() } : {}),
  };
}

/** `date` + `HH:mm` wall-clock inputs to an instant, or null when either is
 * empty or unparseable — the autosave path's only date rule. */
function parseWallClock(date: string | undefined, time: string | undefined): Date | null {
  if (!date || !time) return null;
  const parsed = new Date(`${date}T${time}`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

