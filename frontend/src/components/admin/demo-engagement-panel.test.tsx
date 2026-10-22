import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DemoEngagementPanel } from "@/components/admin/demo-engagement-panel";
import { addDemoEngagement, removeDemoEngagement } from "@/lib/admin";

vi.mock("@/lib/admin", () => ({
  addDemoEngagement: vi.fn(async () => ({ demoAccounts: 20, events: 23, posts: 6, views: 146, interests: 54 })),
  removeDemoEngagement: vi.fn(async () => ({ demoAccounts: 20, events: 23, posts: 6, views: 146, interests: 54 })),
}));

describe("DemoEngagementPanel", () => {
  it("adds demo engagement only after confirming, and reports what it did", async () => {
    render(<DemoEngagementPanel />);

    await userEvent.click(screen.getByRole("button", { name: /Add demo views/ }));
    expect(addDemoEngagement).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: "Add" }));

    expect(addDemoEngagement).toHaveBeenCalledTimes(1);
    expect(await screen.findByRole("status")).toHaveTextContent("Added 146 views and 54 Interested across 23 events and 6 posts.");
  });

  it("removes it after confirming", async () => {
    render(<DemoEngagementPanel />);
    await userEvent.click(screen.getByRole("button", { name: "Remove demo engagement" }));
    await userEvent.click(screen.getByRole("button", { name: "Remove" }));
    expect(removeDemoEngagement).toHaveBeenCalledTimes(1);
  });
});
