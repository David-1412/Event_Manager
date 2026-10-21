import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HeaderSearch, contextFromPath } from "@/components/layout/global-search";

const push = vi.fn();
let pathname = "/messages";
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }), usePathname: () => pathname }));

/** The desktop box (from lg up). jsdom does not apply CSS, so both boxes exist; the
 * desktop one is the first. */
const desktopBox = () => screen.getAllByRole("searchbox", { name: "Search events and posts" })[0];

/**
 * The search lives in the header now: focus shows the All / Events / Posts scope,
 * a chip applies at once when something is typed, and below lg the box folds into a
 * magnifying glass.
 */
describe("HeaderSearch", () => {
  beforeEach(() => {
    push.mockReset();
    pathname = "/messages";
  });

  it("shows the scope chips only while the box is focused", async () => {
    render(<HeaderSearch />);
    expect(screen.queryByRole("radiogroup", { name: "Search in" })).toBeNull();

    await userEvent.click(desktopBox());
    const chips = within(screen.getByRole("radiogroup", { name: "Search in" })).getAllByRole("radio");
    expect(chips.map((c) => c.textContent)).toEqual(["All", "Events", "Posts"]);
    expect(desktopBox()).toHaveAttribute("placeholder", "Search events and posts...");
  });

  it("applies a chip immediately once something is typed", async () => {
    render(<HeaderSearch />);
    await userEvent.type(desktopBox(), "ltb");
    await userEvent.click(screen.getByRole("radio", { name: "Posts" }));
    expect(push).toHaveBeenCalledWith("/forum?q=ltb");
  });

  it("searches everything on Enter by default", async () => {
    render(<HeaderSearch />);
    await userEvent.type(desktopBox(), "netball{Enter}");
    expect(push).toHaveBeenCalledWith("/search?q=netball");
  });

  it("opens a full-width search from the magnifying glass on small screens", async () => {
    render(<HeaderSearch />);
    expect(screen.getAllByRole("searchbox")).toHaveLength(1);
    await userEvent.click(screen.getByRole("button", { name: "Search" }));
    expect(screen.getAllByRole("searchbox")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Close search" })).toBeInTheDocument();
  });
});

describe("contextFromPath", () => {
  it("knows which page a search starts from", () => {
    expect(contextFromPath("/")).toBe("events");
    expect(contextFromPath("/forum")).toBe("forum");
    expect(contextFromPath("/search")).toBe("search");
    expect(contextFromPath("/messages")).toBe("other");
    expect(contextFromPath("/forum/p-1")).toBe("other");
  });
});
