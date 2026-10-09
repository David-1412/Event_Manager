import { describe, expect, it } from "vitest";
import { fixtureDetail, fixtureList } from "@/lib/fixtures";
/**
 * The offline mirror of the server's visibility rule, asserted here because the
 * rule is the whole point of a private event: it leaves the feed but its link
 * still resolves. If fixtureList ever stops filtering, Browse would show events
 * the host hid - and the live API's filter (EventRepository.ApplyFilters) has no
 * test anywhere near it, so this is the closest thing to a guard the repo has.
 */
describe("private events (fixtures)", () => {
  const privateId = "evt-private-friday-pickleball";

  it("keeps a private event out of the browse list", () => {
    const ids = fixtureList().map((event) => event.id);
    expect(ids).not.toContain(privateId);
    expect(fixtureList().every((event) => event.visibility === "Public")).toBe(true);
  });

  it("still resolves it by direct link, which is how sharing works", () => {
    const event = fixtureDetail(privateId);
    expect(event).toBeDefined();
    expect(event?.visibility).toBe("Private");
  });
});
