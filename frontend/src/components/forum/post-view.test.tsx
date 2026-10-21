import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { SWRConfig } from "swr";
import type { ReactNode } from "react";
import { recordForumView } from "@/features/forum/forum-api";
import { useRecordPostView } from "@/features/forum/use-forum";
import { useAuth } from "@/lib/auth/auth-provider";

vi.mock("@/lib/auth/auth-provider", () => ({ useAuth: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock("@/features/forum/forum-api", async (original) => ({
  ...(await original<object>()),
  recordForumView: vi.fn(async () => ({ viewCount: 7, isInterested: true, likeCount: 3 })),
}));

const wrapper = ({ children }: { children: ReactNode }) => (
  <SWRConfig value={{ provider: () => new Map() }}>{children}</SWRConfig>
);

/**
 * Opening a post counts one view - not one per render, not before the session is
 * known (an anonymous view would then be counted for a signed-in reader, and their
 * first-view Interested would never be set).
 */
describe("useRecordPostView", () => {
  beforeEach(() => vi.mocked(recordForumView).mockClear());

  it("waits for the session, then records exactly one view", async () => {
    vi.mocked(useAuth).mockReturnValue({ user: null, loading: true } as never);
    const { rerender } = renderHook(() => useRecordPostView("p-1"), { wrapper });
    expect(recordForumView).not.toHaveBeenCalled();

    vi.mocked(useAuth).mockReturnValue({ user: { id: "u-1" }, loading: false } as never);
    await act(async () => rerender());
    await act(async () => rerender());

    expect(recordForumView).toHaveBeenCalledTimes(1);
    expect(recordForumView).toHaveBeenCalledWith("p-1");
  });

  it("records again for a different post", async () => {
    vi.mocked(useAuth).mockReturnValue({ user: null, loading: false } as never);
    const { rerender } = renderHook(({ id }) => useRecordPostView(id), { wrapper, initialProps: { id: "p-1" } });
    await act(async () => rerender({ id: "p-2" }));
    expect(vi.mocked(recordForumView).mock.calls.map((c) => c[0])).toEqual(["p-1", "p-2"]);
  });
});
