import { describe, expect, it } from "vitest";
import {
  CREATE_EVENT_DEFAULTS,
  createEventSchema,
  toCreateEventPayload,
} from "@/features/create/create-event-schema";

/**
 * A complete, valid set of values; spread and override per case.
 *
 * `CREATE_EVENT_DEFAULTS` alone is deliberately *not* valid - it is the empty
 * form a host starts on, so the fields the form asks people to fill (name,
 * date, venue) are blank. The fixture supplies those.
 */
function valid(overrides: Record<string, unknown> = {}) {
  return {
    ...CREATE_EVENT_DEFAULTS,
    title: "Tuesday Social Badminton",
    date: "2030-06-01",
    venueName: "Queen Victoria Women's Centre",
    address: "210 Latrobe St, Melbourne VIC 3000",
    latitude: -37.8136,
    longitude: 144.9631,
    ...overrides,
  };
}

function errorFor(overrides: Record<string, unknown>) {
  const result = createEventSchema.safeParse(valid(overrides));
  if (result.success) return null;
  return result.error.issues.map((issue) => issue.message).join(" | ");
}

describe("create event validation (spec 7)", () => {
  it("accepts the defaults once a date is chosen", () => {
    expect(createEventSchema.safeParse(valid()).success).toBe(true);
  });

  describe("name", () => {
    it("rejects a name shorter than 3 characters", () => {
      expect(errorFor({ title: "Ru" })).toContain("at least 3 characters");
      expect(errorFor({ title: "  " })).toContain("at least 3 characters");
      // 3 is the first legal length, and the trim happens before the count.
      expect(createEventSchema.safeParse(valid({ title: "Run" })).success).toBe(true);
    });

    it("rejects a name longer than 80 characters", () => {
      expect(errorFor({ title: "a".repeat(121) })).toContain("under 120 characters");
      expect(
        createEventSchema.safeParse(valid({ title: "a".repeat(80) })).success,
      ).toBe(true);
    });
  });

  describe("participants", () => {
    it("requires at least 2 people", () => {
      expect(errorFor({ maxParticipants: 1 })).toContain("At least 2 people");
    });

    it("caps the group at 50", () => {
      expect(errorFor({ maxParticipants: 51 })).toContain("maximum for now");
      expect(
        createEventSchema.safeParse(valid({ maxParticipants: 50 })).success,
      ).toBe(true);
    });

    it("rejects fractional and non-numeric counts", () => {
      expect(errorFor({ maxParticipants: 3.5 })).toContain("Whole people only");
      expect(errorFor({ maxParticipants: "abc" })).toContain(
        "how many people can join",
      );
    });

    it("coerces the numeric text an input gives back to a number", () => {
      const result = createEventSchema.safeParse(valid({ maxParticipants: "10" }));
      expect(result.success).toBe(true);
      if (result.success) expect(result.data.maxParticipants).toBe(10);
    });
  });

  describe("venue", () => {
    it("requires a venue picked from the picker", () => {
      expect(errorFor({ venueName: "" })).toContain("Search for a venue");
      expect(errorFor({ venueName: "   " })).toContain("Search for a venue");
    });

    it("requires the address that came with the picked venue", () => {
      const message = errorFor({ address: "  " });
      expect(message).not.toBeNull();
    });
  });

  describe("time", () => {
    it("requires both a date and a start time", () => {
      expect(errorFor({ date: "" })).toContain("Pick a date");
      expect(errorFor({ startTime: "" })).toContain("Pick a start time");
    });

    it("rejects a finish at or before the start", () => {
      expect(errorFor({ startTime: "18:00", endTime: "18:00" })).toContain(
        "Finish must be after the start",
      );
      expect(errorFor({ startTime: "18:00", endTime: "17:00" })).toContain(
        "Finish must be after the start",
      );
    });

    it("rejects a start in the past", () => {
      expect(errorFor({ date: "2020-01-01" })).toContain("Pick a time in the future");
    });

    it("sends the chosen wall clock as an absolute instant", () => {
      const payload = toCreateEventPayload(
        createEventSchema.parse(
          valid({ date: "2030-06-01", startTime: "18:00", endTime: "20:00" }),
        ),
      );
      // Both ends land on the same day and 2 hours apart, whichever zone the
      // machine is in - which is the only guarantee the payload has to make.
      expect(payload.startAt.endsWith("Z")).toBe(true);
      expect(
        new Date(payload.endAt).getTime() - new Date(payload.startAt).getTime(),
      ).toBe(2 * 60 * 60 * 1000);
      expect(payload.timezone).toBeTruthy();
    });
  });

  describe("cost", () => {
    it("is optional", () => {
      expect(createEventSchema.safeParse(valid({ cost: "" })).success).toBe(true);
    });

    it("rejects non-numeric or negative amounts", () => {
      expect(errorFor({ cost: "abc" })).toContain("Enter a number or leave it empty");
      expect(errorFor({ cost: "-5" })).toContain("Enter a number or leave it empty");
    });

    it("rejects an implausible amount", () => {
      expect(errorFor({ cost: "1001" })).toContain("That looks too high");
    });
  });

  describe("description", () => {
    it("caps it at 1000 characters", () => {
      expect(errorFor({ description: "a".repeat(1001) })).toContain(
        "under 1000 characters",
      );
    });
  });
});

describe("toCreateEventPayload", () => {
  it("sends cost as null when the field is left empty", () => {
    const payload = toCreateEventPayload(createEventSchema.parse(valid({ cost: "" })));
    expect(payload.cost).toBeNull();
  });

  it("sends cost as a number when filled", () => {
    const payload = toCreateEventPayload(
      createEventSchema.parse(valid({ cost: "12.5" })),
    );
    expect(payload.cost).toBe(12.5);
  });

  it("sends description as null when blank rather than an empty string", () => {
    const payload = toCreateEventPayload(
      createEventSchema.parse(valid({ description: "   " })),
    );
    expect(payload.description).toBeNull();
  });
});
