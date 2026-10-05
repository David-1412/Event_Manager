import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { approveDraft } from "@/lib/drafts";
import type { CreateEventPayload } from "@/features/create/create-event-schema";

describe("approveDraft", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response("{}", {
          status: 201,
          headers: { "Content-Type": "application/json" },
        }),
      ),
    );
  });

  afterEach(() => vi.unstubAllGlobals());

  it("sends the event as a JSON object, not a JSON-encoded string", async () => {
    const event: CreateEventPayload = {
      title: "Monday social run",
      tags: [],
      startAt: "2030-10-07T07:00:00.000Z",
      endAt: "2030-10-07T08:00:00.000Z",
      timezone: "Australia/Melbourne",
      venueName: "Glen Waverley Track",
      address: "10 Example Road",
      latitude: -37.88,
      longitude: 145.16,
      maxParticipants: 4,
      cost: null,
      description: null,
    };

    await approveDraft("draft-id", event);

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init?.method).toBe("POST");
    expect(JSON.parse(String(init?.body))).toEqual({ event, note: null });
  });
});