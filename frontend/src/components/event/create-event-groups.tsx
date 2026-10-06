"use client";

import { Field, Input, Select, Textarea } from "@/components/ui/field";
import { TagInput } from "@/components/event/tag-input";
import { ThumbnailInput } from "@/components/event/thumbnail-input";
import { VenueChips, VenuePicker } from "@/components/event/venue-picker";
import type { VenueSelection } from "@/components/event/venue-picker";
import type { CreateEventValues } from "@/features/create/create-event-schema";
import type { Tag } from "@/types/events";

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
  register: <K extends keyof CreateEventValues>(name: K) => object;
  /** Tags are a controlled array, not a registerable native input. */
  onTags: (tags: Tag[]) => void;
  onVenue: (venue: VenueSelection | null) => void;
  /** Uploaded thumbnail URL (or null). Controlled like tags, not a native input. */
  onThumbnail: (url: string | null) => void;
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

function WhatGroup({ values, errorFor, register, onTags, onThumbnail }: CreateGroupsProps) {
  return (
    <fieldset className="flex flex-col gap-4">
      <legend className="text-h3 text-fg">What</legend>

      <Field label="Name" error={errorFor("title")} required>
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


function WhenGroup({ errorFor, register }: CreateGroupsProps) {
  const pair = { sm: "grid-cols-1 gap-4 sm:grid-cols-3" };
  return (
    <fieldset className="flex flex-col gap-4">
      <legend className="text-h3 text-fg">When</legend>
      <div className={"grid " + pair.sm}>
        <Field label="Date" error={errorFor("date")} required>
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
        <Field label="End" error={errorFor("endTime")} required>
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
    </fieldset>
  );
}

function WhereGroup({ venue, errorFor, onVenue }: CreateGroupsProps) {
  return (
    <fieldset className="flex flex-col gap-4">
      <legend className="text-h3 text-fg">Where</legend>
      {/* No venue-name input: the picker reverse-geocodes every pick and writes
          venueName/address itself, so a second box for the same value only ever
          invited a mismatch with the pin. */}
      <Field label="Map location" hint="Drop the pin, move it as needed, then Select." error={errorFor("venueName")} required>
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

function WhoGroup({ errorFor, register }: CreateGroupsProps) {
  return (
    <fieldset className="flex flex-col gap-4">
      <legend className="text-h3 text-fg">Who &amp; how much</legend>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field
          label="Spots"
          hint="Includes you, so 4 means three other people."
          error={errorFor("maxParticipants")}
          required
        >
          {({ id, describedBy, invalid }) => (
            <Input
              id={id}
              type="number"
              inputMode="numeric"
              min={2}
              max={50}
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
