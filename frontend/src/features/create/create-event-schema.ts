import { z } from "zod";
import { SKILL_LEVELS, SPORTS } from "@/lib/sports";
import type { SkillLevel, SportKey } from "@/types/events";

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
      .max(80, "Keep the name under 80 characters"),
    sport: z.enum(SPORTS.map((s) => s.key) as [SportKey, ...SportKey[]], {
      message: "Pick a sport",
    }),
    skillLevel: z.enum(SKILL_LEVELS as [SkillLevel, ...SkillLevel[]], {
      message: "Pick a skill level",
    }),
    date: z.string().min(1, "Pick a date"),
    startTime: z.string().min(1, "Pick a start time"),
    endTime: z.string().min(1, "Pick an end time"),
    venueName: z.string().trim().min(1, "Search for a venue").max(120),
    address: z.string().trim().min(1),
    latitude: z.number(),
    longitude: z.number(),
    maxParticipants: z.coerce
      .number({ message: "Enter how many people can join" })
      .int("Whole people only")
      .min(2, "At least 2 people")
      .max(50, "50 is the maximum for now"),
    cost: z
      .string()
      .trim()
      .optional()
      .refine((v) => !v || (Number.isFinite(Number(v)) && Number(v) >= 0), "Enter a number or leave it empty")
      .refine((v) => !v || Number(v) <= 1000, "That looks too high"),
    description: z.string().trim().max(1000, "Keep it under 1000 characters").optional(),
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
  sport: "badminton",
  skillLevel: "Beginner",
  date: "",
  startTime: "18:00",
  endTime: "20:00",
  venueName: "",
  address: "",
  latitude: 0,
  longitude: 0,
  maxParticipants: 4,
  cost: "",
  description: "",
};

/** What the API expects: `cost` null when empty, dates as ISO UTC. */
export function toCreateEventPayload(values: z.output<typeof createEventSchema>) {
  const start = new Date(`${values.date}T${values.startTime}`);
  const end = new Date(`${values.date}T${values.endTime}`);
  const cost = values.cost && values.cost.trim() ? Number(values.cost) : null;
  return {
    title: values.title,
    sport: values.sport,
    skillLevel: values.skillLevel,
    startAt: start.toISOString(),
    endAt: end.toISOString(),
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || "Australia/Melbourne",
    venueName: values.venueName,
    address: values.address,
    latitude: values.latitude,
    longitude: values.longitude,
    maxParticipants: values.maxParticipants,
    cost,
    description: values.description?.trim() ? values.description.trim() : null,
  };
}

export type CreateEventPayload = ReturnType<typeof toCreateEventPayload>;
