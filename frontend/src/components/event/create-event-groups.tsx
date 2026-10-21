"use client";

import { Repeat } from "lucide-react";
import { Chip } from "@/components/ui/chip";
import { Field, Input, Select, Textarea } from "@/components/ui/field";
import { TagInput } from "@/components/event/tag-input";
import { ThumbnailInput } from "@/components/event/thumbnail-input";
import { VenueChips, VenuePicker } from "@/components/event/venue-picker";
import type { VenueSelection } from "@/components/event/venue-picker";
import { repeatRuleOf, type CreateEventValues } from "@/features/create/create-event-schema";
import {
  DAY_CODES,
  DAY_LETTERS,
  DAY_NAMES,
  FREQUENCY_LABELS,
  MAX_SESSIONS,
  SEMESTER_WEEKS,
  dayCodeOf,
  expandDates,
  parseDay,
  repeatProblem,
  semesterEnd,
  type RepeatFrequency,
} from "@/features/create/recurrence";
import { cn } from "@/lib/cn";
import type { ImportFlagField, Tag } from "@/types/events";

/**
 * The five groups of `/create` (spec §8), each a `fieldset` with a `legend` so
 * assistive tech announces the section the way the design groups it. `Field`
 * supplies the label and aria wiring (spec §7); the controls stay native so
 * mobile gets its own date and time pickers.
 */
export interface CreateGroupsProps {
  values: CreateEventValues;
  venue: VenueSelection | null;
  errorFor: (name: keyof CreateEventValues) => string | undefined;
  /** The "check this" marker for an imported field, if one is still active. */
  flagFor: (field: ImportFlagField) => { text: string; onDismiss: () => void } | undefined;
  register: <K extends keyof CreateEventValues>(name: K) => object;
  /** Tags are a controlled array, not a registerable native input. */
  onTags: (tags: Tag[]) => void;
  onVenue: (venue: VenueSelection | null) => void;
  /** Uploaded thumbnail URL (or null). Controlled like tags, not a native input. */
  onThumbnail: (url: string | null) => void;
  /** The repeat controls that are buttons rather than native inputs (the toggle,
   *  day pills and semester preset) set several fields at once through this. */
  onRepeat: (patch: Partial<CreateEventValues>) => void;
}

export function CreateEventGroups(props: CreateGroupsProps) {
  return (
    <>
      <WhatGroup {...props} />
      <WhenGroup {...props} />
      <WhereGroup {...props} />
      <WhoGroup {...props} />
      <DescriptionGroup {...props} />
    </>
  );
}

function WhatGroup({ values, errorFor, flagFor, register, onTags, onThumbnail }: CreateGroupsProps) {
  return (
    <fieldset className="flex flex-col gap-4">
      <legend className="text-h3 text-fg">What</legend>

      <Field label="Name" error={errorFor("title")} flag={flagFor("title")} required>
        {({ id, describedBy, invalid }) => (
          <Input
            id={id}
            placeholder="Badminton Monday"
            aria-describedby={describedBy}
            aria-invalid={invalid || undefined}
            {...register("title")}
          />
        )}
      </Field>

      {/* Replaces the sport chips and the skill select. Tags are free text, so
          nothing here needs a redeploy to accept a new activity; skill left the
          form entirely because an un-chosen level renders as no badge at all. */}
      <Field label="Tags" error={errorFor("tags")}>
        {({ id }) => (
          <TagInput
            id={id}
            tags={values.tags ?? []}
            onChange={onTags}
            error={errorFor("tags")}
          />
        )}
      </Field>

      {/* Optional photo. Uploads on pick (not on submit), so the live preview and
          the browse card show the real image the moment it is chosen. */}
      <Field label="Photo" hint="JPG, PNG or WebP up to 5 MB. Shown on the event card." optional>
        {({ id }) => (
          <ThumbnailInput id={id} value={values.thumbnailUrl} onChange={onThumbnail} />
        )}
      </Field>
    </fieldset>
  );
}


function WhenGroup(props: CreateGroupsProps) {
  const { errorFor, flagFor, register } = props;
  const pair = { sm: "grid-cols-1 gap-4 sm:grid-cols-3" };
  return (
    <fieldset className="flex flex-col gap-4">
      <legend className="text-h3 text-fg">When</legend>
      <div className={"grid " + pair.sm}>
        <Field label="Date" error={errorFor("date")} flag={flagFor("startAt")} required>
          {({ id, describedBy, invalid }) => (
            <Input
              id={id}
              type="date"
              aria-describedby={describedBy}
              aria-invalid={invalid || undefined}
              {...register("date")}
            />
          )}
        </Field>
        <Field label="Start" error={errorFor("startTime")} required>
          {({ id, describedBy, invalid }) => (
            <Input
              id={id}
              type="time"
              aria-describedby={describedBy}
              aria-invalid={invalid || undefined}
              {...register("startTime")}
            />
          )}
        </Field>
        <Field label="End" error={errorFor("endTime")} flag={flagFor("endAt")} required>
          {({ id, describedBy, invalid }) => (
            <Input
              id={id}
              type="time"
              aria-describedby={describedBy}
              aria-invalid={invalid || undefined}
              {...register("endTime")}
            />
          )}
        </Field>
      </div>
      <RepeatSection {...props} />
    </fieldset>
  );
}

const shortDay = (d: Date) =>
  d.toLocaleDateString("en-AU", { weekday: "short", day: "numeric", month: "short", timeZone: "UTC" });

/** How many session dates the preview lists before "+N more". */
const PREVIEW_DATES = 8;

/**
 * "Repeat event": off by default, and when on, the controls that decide the series -
 * frequency, the days (for weekly and fortnightly), and when it ends - with a
 * semester preset and a live list of the sessions it will create. The server makes
 * one event per session, so what this lists is exactly what appears on Browse.
 */
function RepeatSection({ values, errorFor, register, onRepeat }: CreateGroupsProps) {
  const on = values.repeat === true;
  const frequency: RepeatFrequency = values.repeatFrequency ?? "WEEKLY";
  const first = parseDay(values.date);
  const firstDay = first ? dayCodeOf(first) : null;
  const chosen = new Set(values.repeatDays ?? []);
  const rule = repeatRuleOf(values);
  const sessions = on && first ? expandDates(values.date, rule) : [];
  const problem = on && first ? repeatProblem(values.date, rule) : null;
  const semester = semesterEnd(values.date);
  const semesterOn =
    frequency === "WEEKLY" && values.repeatEnd === "date" && !!semester && values.repeatUntil === semester;

  const toggle = () =>
    onRepeat({
      repeat: !on,
      // Turning it on with no end yet starts from the semester preset, the case
      // this exists for, rather than an empty date the host has to go and find.
      ...(!on && !values.repeatUntil && semester ? { repeatUntil: semester } : {}),
    });

  const toggleDay = (code: string) => {
    const next = new Set(chosen);
    if (next.has(code)) next.delete(code);
    else next.add(code);
    onRepeat({ repeatDays: DAY_CODES.filter((c) => next.has(c)) });
  };

  return (
    <div className={cn("rounded-md border border-border", on && "bg-surface-2")}>
      <button
        type="button"
        role="switch"
        aria-checked={on}
        onClick={toggle}
        className="press flex w-full items-center gap-3 rounded-md p-3 text-left"
      >
        <Repeat aria-hidden="true" className="size-5 shrink-0 text-brand-600" />
        <span className="min-w-0 flex-1">
          <span className="block text-body-strong text-fg">Repeat event</span>
          <span className="block text-meta text-fg-muted">
            A weekly training, a class, a club night - one event per session, each with
            its own spots.
          </span>
        </span>
        <span
          aria-hidden="true"
          className={cn(
            "relative h-6 w-11 shrink-0 rounded-full transition-colors",
            on ? "bg-brand-600" : "bg-border",
          )}
        >
          <span
            className={cn(
              "absolute top-0.5 size-5 rounded-full bg-surface shadow transition-transform",
              on ? "translate-x-[22px]" : "translate-x-0.5",
            )}
          />
        </span>
      </button>

      {on && (
        <div className="flex flex-col gap-4 border-t border-border p-3">
          <div className="flex flex-wrap items-center gap-2">
            <Chip
              selected={semesterOn}
              disabled={!semester}
              title={semester ? undefined : "Pick the first date above first"}
              onClick={() =>
                semester &&
                onRepeat({ repeatFrequency: "WEEKLY", repeatEnd: "date", repeatUntil: semester })
              }
            >
              Semester 1 ({SEMESTER_WEEKS} weeks)
            </Chip>
          </div>

          <Field label="Repeats">
            {({ id }) => (
              <Select id={id} {...register("repeatFrequency")}>
                {(Object.keys(FREQUENCY_LABELS) as RepeatFrequency[]).map((f) => (
                  <option key={f} value={f}>
                    {FREQUENCY_LABELS[f]}
                  </option>
                ))}
              </Select>
            )}
          </Field>

          {frequency !== "MONTHLY" ? (
            <div role="group" aria-labelledby="repeat-days-label" className="flex flex-col gap-2">
              <span id="repeat-days-label" className="text-meta font-medium text-fg">
                On
              </span>
              <div className="flex flex-wrap gap-1.5">
                {DAY_CODES.map((code) => {
                  // The first session's own day is always part of the series (the
                  // server adds it too), so its pill is on and cannot be turned off.
                  const locked = code === firstDay;
                  const pressed = locked || chosen.has(code);
                  return (
                    <button
                      key={code}
                      type="button"
                      aria-pressed={pressed}
                      aria-label={DAY_NAMES[code]}
                      aria-disabled={locked || undefined}
                      title={locked ? "The first session is on a " + DAY_NAMES[code] : DAY_NAMES[code]}
                      onClick={() => !locked && toggleDay(code)}
                      className={cn(
                        "press size-9 rounded-full border text-meta font-semibold transition-colors",
                        pressed
                          ? "border-brand-600 bg-brand-600 text-brand-fg"
                          : "border-border bg-surface text-fg hover:border-fg-muted",
                        locked && "cursor-default",
                      )}
                    >
                      {DAY_LETTERS[code]}
                    </button>
                  );
                })}
              </div>
            </div>
          ) : (
            <p className="text-meta text-fg-muted">
              {first
                ? "On day " + first.getUTCDate() + " of each month. Months without that day are skipped."
                : "On the same day of each month as the first session."}
            </p>
          )}

          <div role="radiogroup" aria-labelledby="repeat-end-label" className="flex flex-col gap-2">
            <span id="repeat-end-label" className="text-meta font-medium text-fg">
              Ends
            </span>
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              <label className="flex cursor-pointer items-center gap-2 text-body text-fg">
                <input type="radio" value="date" className="size-4 accent-brand-600" {...register("repeatEnd")} />
                On date
              </label>
              <label className="flex cursor-pointer items-center gap-2 text-body text-fg">
                <input type="radio" value="count" className="size-4 accent-brand-600" {...register("repeatEnd")} />
                After a number of sessions
              </label>
            </div>
            {values.repeatEnd === "count" ? (
              <Field label="Sessions" hint={"2 to " + MAX_SESSIONS + ", counting the first"} error={errorFor("repeatCount")}>
                {({ id, describedBy, invalid }) => (
                  <Input
                    id={id}
                    type="number"
                    inputMode="numeric"
                    min={2}
                    max={MAX_SESSIONS}
                    aria-describedby={describedBy}
                    aria-invalid={invalid || undefined}
                    {...register("repeatCount")}
                  />
                )}
              </Field>
            ) : (
              <Field label="Last day" error={errorFor("repeatUntil")}>
                {({ id, describedBy, invalid }) => (
                  <Input
                    id={id}
                    type="date"
                    min={values.date || undefined}
                    aria-describedby={describedBy}
                    aria-invalid={invalid || undefined}
                    {...register("repeatUntil")}
                  />
                )}
              </Field>
            )}
          </div>

          <SessionPreview sessions={sessions} problem={problem} hasDate={!!first} />
        </div>
      )}
    </div>
  );
}

/** The sessions the series will create, or why it cannot be created yet. */
function SessionPreview({
  sessions,
  problem,
  hasDate,
}: {
  sessions: Date[];
  problem: string | null;
  hasDate: boolean;
}) {
  if (!hasDate) {
    return <p className="text-meta text-fg-muted">Pick the first date above to see the sessions.</p>;
  }
  if (problem) {
    return (
      <p role="status" className="rounded-md bg-warn-tint px-3 py-2 text-meta text-fg">
        {problem}
      </p>
    );
  }
  const shown = sessions.slice(0, PREVIEW_DATES);
  return (
    <div role="status" className="flex flex-col gap-2">
      <p className="text-meta text-fg">
        <span className="font-semibold">{sessions.length} sessions</span>
        <span className="text-fg-muted">
          {" \u00b7 "}
          {shortDay(sessions[0])} to {shortDay(sessions[sessions.length - 1])}
        </span>
      </p>
      <ul className="flex flex-wrap gap-1.5">
        {shown.map((d) => (
          <li
            key={d.toISOString()}
            className="rounded-full bg-surface px-2 py-0.5 text-micro text-fg-muted ring-1 ring-border"
          >
            {shortDay(d)}
          </li>
        ))}
        {sessions.length > shown.length && (
          <li className="px-1 py-0.5 text-micro text-fg-muted">+{sessions.length - shown.length} more</li>
        )}
      </ul>
    </div>
  );
}

function WhereGroup({ venue, errorFor, flagFor, onVenue }: CreateGroupsProps) {
  return (
    <fieldset className="flex flex-col gap-4">
      <legend className="text-h3 text-fg">Where</legend>
      {/* No venue-name input: the picker reverse-geocodes every pick and writes
          venueName/address itself, so a second box for the same value only ever
          invited a mismatch with the pin. */}
      <Field
        label="Map location"
        hint="Drop the pin, move it as needed, then Select."
        error={errorFor("venueName")}
        flag={flagFor("venue")}
        required
      >
        {() =>
          venue ? (
            // The Field already shows the error once; don't pass it down or the
            // picker would render a second identical red line.
            <VenueChips venue={venue} onChange={onVenue} />
          ) : (
            <VenuePicker value={null} onChange={onVenue} />
          )
        }
      </Field>
    </fieldset>
  );
}

function WhoGroup({ errorFor, flagFor, register }: CreateGroupsProps) {
  return (
    <fieldset className="flex flex-col gap-4">
      <legend className="text-h3 text-fg">Who &amp; how much</legend>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field
          label="Spots"
          hint="Includes you, so 4 means three other people. Leave empty for no limit."
          error={errorFor("maxParticipants")}
          flag={flagFor("maxParticipants")}
          optional
        >
          {({ id, describedBy, invalid }) => (
            <Input
              id={id}
              type="number"
              inputMode="numeric"
              min={2}
              max={50}
              placeholder="No limit"
              aria-describedby={describedBy}
              aria-invalid={invalid || undefined}
              {...register("maxParticipants")}
            />
          )}
        </Field>
        <Field label="Cost per person" hint="Leave empty for a free event." optional>
          {({ id, describedBy, invalid }) => (
            <Input
              id={id}
              inputMode="decimal"
              placeholder="15"
              aria-describedby={describedBy}
              aria-invalid={invalid || undefined}
              {...register("cost")}
            />
          )}
        </Field>
      </div>

      {/* Discovery, not a password: a private event still opens for anyone holding
          its link, it just stays out of Browse. The hint has to say that, because
          "Private" otherwise reads as "only I can see it" - which is not what
          happens, and the host would be waiting for people the link already lets in. */}
      <Field
        label="Who can find it"
        hint="Private events skip Browse - share the link from the event page instead."
        error={errorFor("visibility")}
        required
      >
        {({ id, describedBy, invalid }) => (
          <Select
            id={id}
            aria-describedby={describedBy}
            aria-invalid={invalid || undefined}
            {...register("visibility")}
          >
            <option value="Public">Public - listed on Browse</option>
            <option value="Private">Private - link only</option>
          </Select>
        )}
      </Field>

      {/* Moderation, not discovery. Kept next to visibility because both answer
          "who gets in", and a host deciding one is thinking about the other. The
          label states the consequence - a request is not a spot - so nobody ticks
          it believing they have limited the headcount. */}
      <label className="flex cursor-pointer items-start gap-3 rounded-md border border-border p-3">
        <input
          type="checkbox"
          className="mt-0.5 size-4 accent-brand-600"
          {...register("requiresJoinApproval")}
        />
        <span className="min-w-0">
          <span className="block text-body-strong text-fg">Approve members first</span>
          <span className="block text-meta text-fg-muted">
            People ask to join and you approve or decline each one. Until you approve
            them they have no spot, so the count only moves when you say yes.
          </span>
        </span>
      </label>
    </fieldset>
  );
}

function DescriptionGroup({
  values,
  errorFor,
  register,
}: CreateGroupsProps) {
  return (
    <fieldset className="flex flex-col gap-4">
      <legend className="text-h3 text-fg">Description</legend>
      <Field
        label="What should people bring or expect?"
        hint={`${(values.description ?? "").length}/1000`}
        error={errorFor("description")}
        optional
      >
        {({ id, describedBy, invalid }) => (
          <Textarea
            id={id}
            placeholder="Two courts booked 6-8pm. Bring your own racket."
            aria-describedby={describedBy}
            aria-invalid={invalid || undefined}
            {...register("description")}
          />
        )}
      </Field>
    </fieldset>
  );
}
