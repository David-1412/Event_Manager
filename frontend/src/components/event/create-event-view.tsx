"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { EventCard } from "@/components/event/event-card";
import { type VenueSelection } from "@/components/event/venue-picker";
import { toast } from "@/components/ui/toast";
import { ApiError, request, usingFixtures } from "@/lib/api";
import {
  CREATE_EVENT_DEFAULTS,
  createEventSchema,
  toCreateEventPayload,
  type CreateEventValues,
} from "@/features/create/create-event-schema";
import { melbourneDateInputValue } from "@/lib/format";
import { CreateEventGroups } from "@/components/event/create-event-groups";
import { CreateEventFooter } from "@/components/event/create-event-footer";
import { previewOf } from "@/components/event/create-event-preview";

/**
 * `/create` (spec §8): five groups - What / When / Where / Who and how much /
 * Description - with a live preview card pinned top-right at `lg+`. Errors show
 * on blur or submit, never mid-first-word; a server `422` lands on the matching
 * `Field`; the summary at the top makes the failure unmistakable.
 */
export function CreateEventView() {
  const router = useRouter();
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
  // eslint-disable-next-line react-hooks/incompatible-library
  const values = form.watch();

  const errorFor = (name: keyof CreateEventValues) =>
    form.formState.errors[name]?.message ?? serverErrors[name];

  function applyVenue(next: VenueSelection | null) {
    setVenue(next);
    form.setValue("venueName", next?.venueName ?? "", { shouldValidate: true });
    form.setValue("address", next?.address ?? "");
    form.setValue("latitude", next?.latitude ?? 0);
    form.setValue("longitude", next?.longitude ?? 0);
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
    try {
      const created = await postEvent(toCreateEventPayload({ ...parsed.data, ...venue }));
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
        <CreateEventGroups
          values={values}
          venue={venue}
          errorFor={errorFor}
          register={form.register}
          onSelectSport={(sport) => form.setValue("sport", sport, { shouldValidate: true })}
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

      <CreateEventFooter submitting={form.formState.isSubmitting} />
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
