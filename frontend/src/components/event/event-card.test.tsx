import { describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { EventPreviewPanel } from "@/components/event/event-preview-panel";
import { EventCard, eventCardVariant } from "@/components/event/event-card";
import { isPublishedStatus } from "@/types/events";
import type { EventListItem } from "@/types/events";

/**
 * The review workflow's client-side half. `eventCardVariant` is what decides
 * whether a card advertises open slots, so the ordering here is the difference
 * between "come join this" and "this is waiting on a decision".
 */
function item(overrides: Partial<EventListItem> = {}): EventListItem {
  return {
    id: "evt-1",
    title: "Sunday social",
    description: null,
    tags: ["social"],
    sportIcon: null,
    skillLevel: null,
    startAt: "2026-11-01T09:00:00.000Z",
    endAt: "2026-11-01T11:00:00.000Z",
    timezone: "Australia/Melbourne",
    venueName: "Test courts",
    address: "1 Test St",
    thumbnailUrl: null,
    latitude: 0,
    longitude: 0,
    cost: null,
    maxParticipants: 10,
    joinedCount: 2,
    interestedCount: 1,
    status: "Scheduled",
    isCancelled: false,
    visibility: "Public",
    distanceKm: null,
    ...overrides,
  };
}

describe("isPublishedStatus", () => {
  it("treats Scheduled and Published as the two live statuses", () => {
    expect(isPublishedStatus("Scheduled")).toBe(true);
    expect(isPublishedStatus("Published")).toBe(true);
    expect(isPublishedStatus("PendingReview")).toBe(false);
    expect(isPublishedStatus("Draft")).toBe(false);
    expect(isPublishedStatus("Rejected")).toBe(false);
    expect(isPublishedStatus("Cancelled")).toBe(false);
    expect(isPublishedStatus("Completed")).toBe(false);
  });
});

describe("eventCardVariant with a pending-review event", () => {
  it("reports pending-review rather than mine for the host", () => {
    // The case that matters: a host must not see "You're hosting" plus open
    // availability on an event nobody can find or join yet.
    expect(eventCardVariant(item({ status: "PendingReview" }), { isHost: true })).toBe(
      "pending-review",
    );
  });

  it("reports pending-review rather than an availability variant", () => {
    expect(eventCardVariant(item({ status: "PendingReview", joinedCount: 10 }))).toBe(
      "pending-review",
    );
    expect(
      eventCardVariant(item({ status: "PendingReview", joinedCount: 9 }), { isJoined: true }),
    ).toBe("pending-review");
  });

  it("outranks Completed but loses to Cancelled", () => {
    // Cancelled wins because a withdrawn event is a stronger fact than a
    // submission still in flight; pending beats Completed because an event
    // awaiting review has not happened.
    expect(
      eventCardVariant(item({ status: "PendingReview", isCancelled: true }), { isHost: true }),
    ).toBe("cancelled");
    expect(eventCardVariant(item({ status: "Completed" }), { isHost: true })).toBe("ended");
  });

  it("leaves the live statuses on their existing variants", () => {
    expect(eventCardVariant(item(), { isHost: true })).toBe("mine");
    expect(eventCardVariant(item({ status: "Published" }), { isHost: true })).toBe("mine");
    expect(eventCardVariant(item({ status: "Rejected" }), { isHost: true })).toBe("mine");
  });
});

/**
 * The browse interaction model: first activation selects, the second opens.
 *
 * These exist because the card previously routed everything through a title link
 * with an inset `after:absolute after:inset-0` overlay. That overlay covered the
 * whole card, so the click never reached the card's own handler and the first
 * click navigated - the exact behaviour the preview panel replaces. The
 * "renders no link when selectable" test is the guard against that regressing
 * back into the card.
 */
describe("EventCard activation", () => {
  function renderCard(props: Parameters<typeof EventCard>[0]) {
    render(<EventCard {...props} />);
    return screen.getByRole("button", { name: /Sunday social/i });
  }

  it("selects on the first activation without opening", () => {
    const onSelect = vi.fn();
    const onOpen = vi.fn();
    const card = renderCard({ event: item(), onSelect, onOpen });

    fireEvent.click(card);
    expect(onSelect).toHaveBeenCalledWith("evt-1");
    expect(onOpen).not.toHaveBeenCalled();
  });

  it("opens on the second activation of the selected card", () => {
    const onSelect = vi.fn();
    const onOpen = vi.fn();
    const card = renderCard({ event: item(), onSelect, onOpen, selected: true });

    fireEvent.click(card);
    expect(onOpen).toHaveBeenCalledWith("evt-1");
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("activates with Enter", () => {
    const onSelect = vi.fn();
    const onOpen = vi.fn();
    const card = renderCard({ event: item(), onSelect, onOpen });
    fireEvent.keyDown(card, { key: "Enter" });
    expect(onSelect).toHaveBeenCalledWith("evt-1");
    expect(onOpen).not.toHaveBeenCalled();
  });

  it("opens with Enter on the already-selected card", () => {
    const onSelect = vi.fn();
    const onOpen = vi.fn();
    const card = renderCard({ event: item(), onSelect, onOpen, selected: true });
    fireEvent.keyDown(card, { key: "Enter" });
    expect(onOpen).toHaveBeenCalledWith("evt-1");
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("activates on Space and stops it scrolling the list", () => {
    const onSelect = vi.fn();
    const card = renderCard({ event: item(), onSelect, onOpen: vi.fn() });
    const event = fireEvent.keyDown(card, { key: " " });
    expect(onSelect).toHaveBeenCalledWith("evt-1");
    // fireEvent returns false once preventDefault() has been applied.
    expect(event).toBe(false);
  });

  it("ignores keys that are not an activation", () => {
    const onSelect = vi.fn();
    const card = renderCard({ event: item(), onSelect, onOpen: vi.fn() });
    fireEvent.keyDown(card, { key: "a" });
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("is reachable and announces its pressed state", () => {
    render(<EventCard event={item()} selected onSelect={vi.fn()} onOpen={vi.fn()} />);
    const card = screen.getByRole("button", { name: /Sunday social/i });
    expect(card).toHaveAttribute("aria-pressed", "true");
    expect(card).toHaveAttribute("tabindex", "0");
  });

  it("renders no navigate-on-click link when it is selectable", () => {
    render(<EventCard event={item()} onSelect={vi.fn()} onOpen={vi.fn()} />);
    expect(screen.queryByRole("link")).toBeNull();
  });

  it("stays a plain link everywhere else, so /my-events still navigates", () => {
    render(<EventCard event={item()} />);
    expect(screen.getByRole("link", { name: "Sunday social" })).toHaveAttribute(
      "href",
      "/events/evt-1",
    );
  });
});

/**
 * The preview's host line.
 *
 * The host arrives on the feed row, so the panel must never invent one. It used
 * to fall back to "Monash Sport", which is a real club name and therefore reads
 * as a true statement rather than a gap - a preview that attributes an event to
 * the wrong person is worse than one that admits it has no host.
 */
describe("EventPreviewPanel host", () => {
  function renderPanel(overrides: Partial<EventListItem> = {}) {
    render(
      <EventPreviewPanel
        event={item(overrides)}
        onOpen={vi.fn()}
        onClose={vi.fn()}
      />,
    );
  }

  it("names the host from the feed row", () => {
    renderPanel({ hostName: "Priya Raman", hostPhotoUrl: null });
    expect(screen.getByRole("complementary").textContent).toContain("Priya Raman");
  });

  it("renders the host photo when the feed row carries one", () => {
    renderPanel({ hostName: "Priya Raman", hostPhotoUrl: "/uploads/priya.png" });
    const img = screen.getByTitle("Priya Raman").querySelector("img");
    expect(img).not.toBeNull();
    expect(img?.getAttribute("src")).toContain("/uploads/priya.png");
  });

  it("falls back to initials, not a broken image, when there is no photo", () => {
    renderPanel({ hostName: "Priya Raman", hostPhotoUrl: null });
    expect(screen.getByTitle("Priya Raman")).toHaveTextContent("PR");
    expect(screen.getByTitle("Priya Raman").querySelector("img")).toBeNull();
  });

  it("never substitutes a placeholder club name", () => {
    renderPanel({ hostName: null });
    const text = screen.getByRole("complementary").textContent ?? "";
    expect(text).not.toMatch(/Monash Sport/i);
  });

  it("says so when the feed row has no host", () => {
    renderPanel({ hostName: null });
    const panel = screen.getByRole("complementary");
    expect(panel.textContent).toContain("Not stated");
    expect(panel.textContent).not.toContain("Priya");
  });

  it("treats a whitespace-only name as no host", () => {
    renderPanel({ hostName: "   " });
    expect(screen.getByRole("complementary").textContent).toContain("Not stated");
  });
});

/**
 * The compact density the browse redesign uses. What matters here is not pixels
 * but the contract that makes the density safe to ship on a shared component:
 * `default` must render exactly what it always did, and `compact` must move the
 * social proof up without dropping it or duplicating it.
 */
describe("EventCard density", () => {
  /** The compact social-proof row. Matched on the sr-only capacity sentence,
   * since "joined" alone is split across spans by that same sr-only text. */
  function proofRow(container: HTMLElement): Element | undefined {
    return Array.from(container.querySelectorAll("p")).find((p) =>
      /spots taken|people joined/.test(p.textContent ?? ""),
    );
  }
  it("shows the joined and interested tally under the title in compact", () => {
    render(
      <EventCard
        event={item({ joinedCount: 8, maxParticipants: 12, interestedCount: 5 })}
        density="compact"
        onSelect={vi.fn()}
        onOpen={vi.fn()}
      />,
    );
    const proof = proofRow(document.body);
    expect(proof?.textContent).toContain("8/12");
    expect(proof?.textContent).toContain("joined");
    expect(proof?.textContent).toContain("5");
    expect(proof?.textContent).toContain("interested");
  });

  it("does not repeat the tally in the compact footer", () => {
    render(
      <EventCard
        event={item({ joinedCount: 8, maxParticipants: 12, interestedCount: 5 })}
        density="compact"
        onSelect={vi.fn()}
        onOpen={vi.fn()}
      />,
    );
    // Exactly one proof row on the whole card. Two would mean the old footer tally
    // survived alongside the new row, duplicating both numbers.
    expect(document.querySelectorAll("p").length).toBeGreaterThan(0);
    expect(
      [...document.querySelectorAll("p")].filter((el) =>
        /spots taken/.test(el.textContent ?? ""),
      ),
    ).toHaveLength(1);
    expect(document.body.textContent).not.toMatch(/interested\s+interested/);
  });

  it("keeps the footer tally and hero count in the default density", () => {
    const { container } = render(
      <EventCard event={item({ joinedCount: 8, maxParticipants: 10 })} />,
    );
    // The default card is what /my-events, the profile page and the create preview
    // render; it must be untouched by the compact work.
    expect(container.textContent).toContain("joined");
    expect(within(container).getByText("8/10")).toBeInTheDocument();
    // The footer tally splits the glyph, the number and the word across spans,
    // so it is asserted as text rather than as a single matched node.
    expect(container.textContent).toMatch(/1\s+interested/);
  });

  it("renders an unlimited compact card without a denominator", () => {
    render(
      <EventCard
        event={item({ maxParticipants: null, joinedCount: 3 })}
        density="compact"
        onSelect={vi.fn()}
        onOpen={vi.fn()}
      />,
    );
    const proof = proofRow(document.body);
    expect(proof?.textContent).toContain("3");
    expect(proof?.textContent).not.toContain("3/");
  });

  it("keeps the two-stage activation in compact", () => {
    const onSelect = vi.fn();
    const onOpen = vi.fn();
    // cleanup() because two cards in one document would make getByRole ambiguous.
    cleanup();
    render(<EventCard event={item()} density="compact" onSelect={onSelect} onOpen={onOpen} />);
    fireEvent.click(screen.getByRole("button", { name: /Sunday social/i }));
    expect(onSelect).toHaveBeenCalledWith("evt-1");
    expect(onOpen).not.toHaveBeenCalled();

    cleanup();
    render(
      <EventCard
        event={item()}
        density="compact"
        selected
        onSelect={onSelect}
        onOpen={onOpen}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /Sunday social/i }));
    expect(onOpen).toHaveBeenCalledWith("evt-1");
  });

  it("still names the capacity in the accessible label", () => {
    render(
      <EventCard
        event={item({ joinedCount: 8, maxParticipants: 12 })}
        density="compact"
        onSelect={vi.fn()}
        onOpen={vi.fn()}
      />,
    );
    expect(
      screen.getByRole("button", { name: /8 of 12 spots taken/ }),
    ).toBeInTheDocument();
  });
});


/**
 * The compact card's thumbnail, pinned because its size is no longer purely
 * cosmetic: the virtualized list windows on a fixed ROW_HEIGHT, and the thumb is
 * what sets that height once it outgrows the text column. Asserted as class names
 * because jsdom lays nothing out, so this can only pin the contract - that the
 * image is square, responsive and cropping - not the pixels.
 */
describe("EventCard compact thumbnail", () => {
  function thumbClasses(event: Partial<EventListItem> = {}): string {
    const { container } = render(
      <EventCard
        event={item(event)}
        density="compact"
        onSelect={vi.fn()}
        onOpen={vi.fn()}
      />,
    );
    // The poster is the article's last child and the only aria-hidden box in it.
    const box = container.querySelector("article > div[aria-hidden]");
    expect(box).not.toBeNull();
    return box?.getAttribute("class") ?? "";
  }

  it("is a square that scales up at lg rather than a fixed 56px box", () => {
    const cls = thumbClasses();
    expect(cls).toContain("aspect-square");
    expect(cls).toContain("w-20");
    expect(cls).toContain("lg:w-32");
    // The old fixed pair must not come back alongside the aspect ratio.
    expect(cls).not.toMatch(/h-14/);
  });

  it("cannot be squeezed out of shape by a long title", () => {
    // shrink-0 is what stops flex-1 on the text column from eating the width.
    expect(thumbClasses({ title: "A ".repeat(120) })).toContain("shrink-0");
  });

  it("centres against the text block instead of hanging off the top", () => {
    expect(thumbClasses()).toContain("self-center");
  });

  it("crops the image to fill the square without distortion", () => {
    render(
      <EventCard
        event={item({ thumbnailUrl: "/uploads/poster.png" })}
        density="compact"
        onSelect={vi.fn()}
        onOpen={vi.fn()}
      />,
    );
    const img = document.querySelector("article > div[aria-hidden] img");
    expect(img?.getAttribute("src")).toContain("/uploads/poster.png");
    expect(img?.getAttribute("class")).toContain("object-cover");
    expect(img?.getAttribute("class")).toContain("h-full");
  });

  it("leaves the default density poster alone", () => {
    const { container } = render(<EventCard event={item()} />);
    const cls =
      container.querySelector("article > div[aria-hidden]")?.getAttribute("class") ?? "";
    expect(cls).toContain("md:w-70");
    expect(cls).not.toContain("aspect-square");
    expect(cls).not.toContain("self-center");
  });
});