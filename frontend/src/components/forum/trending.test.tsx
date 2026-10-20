import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { rollingRows } from "@/components/forum/TrendingBanner";
import { FireButton } from "@/components/ui/fire-button";
import { trendingHref } from "@/features/forum/forum-api";

const ten = Array.from({ length: 10 }, (_, i) => `item ${i + 1}`);
const ranks = (start: number) => rollingRows(ten, start).map((r) => r.rank);

/**
 * The roll is the part of Monash Pulse with an exact spec: three rows, one step a
 * second, 1,2,3 -> 2,3,4 -> ... -> 9,10,1 -> 10,1,2 and round again.
 */
describe("rollingRows", () => {
  it("starts on the top three", () => {
    expect(ranks(0)).toEqual([1, 2, 3]);
  });

  it("advances one row per step", () => {
    expect(ranks(1)).toEqual([2, 3, 4]);
    expect(ranks(7)).toEqual([8, 9, 10]);
  });

  it("wraps past the end, keeping each item's real rank", () => {
    expect(ranks(8)).toEqual([9, 10, 1]);
    expect(ranks(9)).toEqual([10, 1, 2]);
    expect(ranks(10)).toEqual([1, 2, 3]);
  });

  it("returns a short list whole instead of rolling it", () => {
    expect(rollingRows(["a", "b"], 5).map((r) => r.rank)).toEqual([1, 2]);
    expect(rollingRows(["a", "b", "c"], 1).map((r) => r.rank)).toEqual([1, 2, 3]);
  });
});

describe("trendingHref", () => {
  it("links events to the event page and posts to the forum", () => {
    expect(trendingHref({ kind: "event", id: "e1", title: "", score: 1 })).toBe("/events/e1");
    expect(trendingHref({ kind: "post", id: "p1", title: "", score: 1 })).toBe("/forum/p1");
  });
});

describe("FireButton", () => {
  it("announces its state with aria-pressed, not only colour", () => {
    const { rerender } = render(<FireButton label="Interested" />);
    expect(screen.getByRole("button", { name: /Interested/ })).toHaveAttribute("aria-pressed", "false");
    rerender(<FireButton label="Interested" active />);
    expect(screen.getByRole("button", { name: /Interested/ })).toHaveAttribute("aria-pressed", "true");
  });

  it("switches to the active label", () => {
    render(<FireButton label="Follow" activeLabel="Following" active />);
    expect(screen.getByRole("button")).toHaveTextContent("Following");
  });

  it("shows the count and is disabled while loading", () => {
    const onClick = vi.fn();
    render(<FireButton label="Interested" count={1234} loading onClick={onClick} />);
    const button = screen.getByRole("button");
    expect(button).toHaveTextContent("1,234");
    expect(button).toBeDisabled();
  });
});
