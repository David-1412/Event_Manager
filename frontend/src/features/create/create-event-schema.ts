import { z } from "zod";
import { TAG_MAX_PER_EVENT, normalizeTags } from "@/lib/sports";
import type { Tag } from "@/types/events";


/**
 * Client validation mirrors the API's (spec §8) so the two never disagree:
 * the same bounds appear in the API's FluentValidation rules, and a server
 * `422` still lands on the matching `Field`.
 *
 * Times are validated against Melbourne wall-clock strings because that is what
 * the native inputs produce; `lib/format.ts` remains the only place timezone
 * conversion happens (spec §12).
 */
export const createEventSchema = z
  .object({
    title: z
      .string()
      .trim()
      .min(3, "Give the event a name of at least 3 characters")
      .max(120, "Keep the name under 120 characters"),
    /**
     * Free tags. Optional and empty by default. Entries are normalized in
     * superRefine, which both rejects a malformed one (the same rule the server
     * applies) and writes the normalized set back, so what the user typed and what
     * gets POSTed cannot disagree.
     */
    tags: z.array(z.string()).max(TAG_MAX_PER_EVENT, `Use at most ${TAG_MAX_PER_EVENT} tags`).optional(),
    date: z.string().min(1, "Pick a date"),

    startTime: z.string().min(1, "Pick a start time"),
    endTime: z.string().min(1, "Pick an end time"),
    venueName: z.string().trim().min(1, "Search for a venue").max(120),
    address: z.string().trim().min(1),
    // Optional upload. The form stores the URL the upload endpoint returned (or
    // null), never the bytes, so there is nothing to validate beyond its shape -
    // the image itself was size/type-checked when it was uploaded.
    thumbnailUrl: z.string().trim().max(400).nullish(),
    latitude: z.number(),
    longitude: z.number(),
    /**
     * Empty means no limit, so the value is `number | null` rather than a number.
     *
     * Three shapes were wrong before this one settled, all worth recording:
     *
     *  - `z.coerce.number().optional()` - `z.coerce.number("")` is `0`, which passes
     *    `.optional()` and stores a capacity of zero: an event born full, the
     *    opposite of what an empty box means.
     *  - `z.preprocess(emptyToNull, z.number()...)` - preprocess rewrites "" to null
     *    and then hands null to a non-nullable number, so it fails as "Invalid
     *    input" and the empty case never reaches the chain at all.
     *  - `z.union([z.null(), z.coerce.number()...])` - reads best, but a failed union
     *    member is reported as a generic "Invalid input" because a union cannot say
     *    which arm was meant. "Whole people only" and "Enter how many people can
     *    join" both disappear, and the user is left with a message that names
     *    nothing. A test caught exactly this.
     *
     * So: accept the raw field, then decide in one place. `unknown` is deliberate -
     * a typed schema here would have to pass the empty string through a number
     * check to get to the branch that handles it. `superRefine` returns early so a
     * cleared box does not additionally fail the bounds, and a value that arrives
     * as numeric text ("10" from an input) is coerced here rather than by the
     * schema, which is what makes the empty and numeric cases distinguishable.
     */
    maxParticipants: z.unknown().superRefine((raw, ctx) => {
      const add = (message: string) => ctx.addIssue({ code: "custom", message });

      // A number input reports "" when cleared, and null/undefined cover a field
      // dropped by a draft or an import. All three mean "no limit".
      if (raw === "" || raw === null || raw === undefined) return;

      const value = Number(raw);
      if (!Number.isFinite(value)) {
        add("Enter how many people can join");
        return;
      }
      if (!Number.isInteger(value)) {
        add("Whole people only");
        return;
      }
      if (value < 2) {
        add("At least 2 people");
        return;
      }
      if (value > 50) add("50 is the maximum for now");
    })
      // Validation above only decides whether the form may be submitted; this is
      // what the payload actually carries. Without it an empty box would reach the
      // API as "" instead of null, and "" is not a value the server can store.
      .transform((raw) =>
        raw === "" || raw === null || raw === undefined ? null : Number(raw),
      ),
    cost: z
      .string()
      .trim()
      .optional()
      .refine((v) => !v || (Number.isFinite(Number(v)) && Number(v) >= 0), "Enter a number or leave it empty")
      .refine((v) => !v || Number(v) <= 1000, "That looks too high"),
    description: z.string().trim().max(1000, "Keep it under 1000 characters").optional(),
    /**
     * Discovery choice, mirrored by the API's `Visibility` rule. A native select
     * always sends one of the two names, so this never arrives malformed from the
     * form; the enum still guards a programmatically-set value against the same
     * set the server accepts.
     */
    visibility: z.enum(["Public", "Private"], { message: "Pick public or private" }),

  /** Opt-in moderation. Defaults false so a form that never shows the control —
   *  or a draft saved before it existed — produces an open event. */
  requiresJoinApproval: z.boolean().optional(),
  })
  .superRefine((values, ctx) => {
    const start = new Date(`${values.date}T${values.startTime}`);
    const end = new Date(`${values.date}T${values.endTime}`);
    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) {
      ctx.addIssue({ code: "custom", path: ["startTime"], message: "That date or time is not valid" });
      return;
    }
    if (end <= start) {
      ctx.addIssue({
        code: "custom",
        path: ["endTime"],
        message: "Finish must be after the start",
      });
    }
    if (start.getTime() < Date.now() - 60_000) {
      ctx.addIssue({
        code: "custom",
        path: ["date"],
        message: "Pick a time in the future",
      });
    }
  });

export type CreateEventValues = z.input<typeof createEventSchema>;

export const CREATE_EVENT_DEFAULTS: CreateEventValues = {
  title: "",
  tags: [],
  date: "",
  startTime: "18:00",
  endTime: "20:00",
  venueName: "",
  address: "",
  thumbnailUrl: null,
  latitude: 0,
  longitude: 0,
  // No capacity by default: an event the host did not think about should not
  // quietly cap itself at four, which is what the old default did to every form
  // submitted without looking at the field.
  maxParticipants: null,
  cost: "",
  description: "",
  // Public by default: an event the host forgot to think about should be
  // findable, not silently hidden from everyone including their friends.
  visibility: "Public",

  // Open by default, for the opposite reason to the one above: gating joins is a
  // rule the host has to choose, and a default of true would make an event nobody
  // thought about impossible to join without anyone telling them why.
  requiresJoinApproval: false,
};

/**
 * What the API expects: `cost` null when empty, dates as ISO UTC, tags normalized.
 *
 * Tags are normalized here rather than in the schema because superRefine cannot
 * rewrite a value; the tag input already stores normalized chips, so this is a
 * final dedupe/cap against a pasted or programmatically-set value and must apply
 * the identical rule the server enforces.
 *
 * `skillLevel` is omitted entirely: the form dropped its selector, and the API
 * treats a missing skill as null and hides the badge.
 */
export function toCreateEventPayload(values: z.output<typeof createEventSchema>) {
  const start = new Date(`${values.date}T${values.startTime}`);
  const end = new Date(`${values.date}T${values.endTime}`);
  const cost = values.cost && values.cost.trim() ? Number(values.cost) : null;
  const tags: Tag[] = normalizeTags(values.tags ?? []);
  return {
    title: values.title,
    tags,
    startAt: start.toISOString(),
    endAt: end.toISOString(),
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || "Australia/Melbourne",
    venueName: values.venueName,
    address: values.address,
    thumbnailUrl: values.thumbnailUrl?.trim() ? values.thumbnailUrl.trim() : null,
    latitude: values.latitude,
    longitude: values.longitude,
    // null when the host left Spots empty, which the API stores as "no limit".
    maxParticipants: values.maxParticipants,
    cost,
    description: values.description?.trim() ? values.description.trim() : null,
    visibility: values.visibility,
    requiresJoinApproval: values.requiresJoinApproval ?? false,
  };
}


export type CreateEventPayload = ReturnType<typeof toCreateEventPayload>;
