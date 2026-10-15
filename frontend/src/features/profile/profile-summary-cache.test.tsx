import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { SWRConfig } from "swr";
import { createElement, type ReactNode } from "react";
import { profileApi, profileSummaryKey } from "./profile-api";
import { useProfileSummary } from "./use-profile";
import { useFollowProfile } from "./use-follow";
import { useAuth } from "@/lib/auth/auth-provider";
import * as api from "@/lib/api";
import type { ProfileSummary } from "@/types/profile";

/**
 * The follow write and the profile read must address one cache slot.
 *
 * The bug this guards was invisible in every other test because each half was
 * individually correct. `useProfileSummary` subscribed to
 * `profileSummaryKey(id, viewerId)` while `profileApi.summary(id)` fetched
 * `profileSummaryKey(id)`. The follow mutation then wrote the server's answer into the
 * *partitioned* key with `revalidate: false` - a slot with no subscriber. So the request
 * succeeded, the server answered with the right `isFollowing`, the follower count moved
 * on the next page load, and the button kept saying "Follow" until that reload.
 *
 * A test asserting only "the mutation wrote the right value" passes on the broken code,
 * because it did write the right value - to the wrong place. What has to be asserted is
 * that the slot the reader subscribed to is the slot the writer wrote. The first block
 * pins that directly; the second observes it through a rendered reader.
 */

vi.mock("@/lib/auth/auth-provider");
const mockedAuth = vi.mocked(useAuth);

const CREATOR = "creator-1";
const VIEWER = "viewer-9";

function summary(isFollowing: boolean, followersCount: number): ProfileSummary {
  return {
    userId: CREATOR,
    displayName: "Casey Rivera",
    avatarUrl: null,
    role: "Member",
    bio: null,
    organizationName: null,
    profileKind: "Person",
    memberSince: "2026-01-01T00:00:00Z",
    stats: { eventsCreated: 4, upcomingEvents: 2, interestedEvents: 1, joinedEvents: 3 },
    followersCount,
    isFollowing,
  };
}

function wrapper({ children }: { children: ReactNode }) {
  return createElement(SWRConfig, { value: { revalidateOnFocus: false } }, children);
}

function signedIn(id: string) {
  return {
    user: {
      uid: `firebase-${id}`,
      id,
      displayName: "Test Viewer",
      email: "viewer@example.com",
      photoUrl: null,
      role: "Member",
    },
    loading: false,
    signOut: vi.fn(),
  } as unknown as ReturnType<typeof useAuth>;
}

function stubSummaryFetch(isFollowing = false, followersCount = 10) {
  return vi.spyOn(globalThis, "fetch").mockResolvedValue(
    new Response(JSON.stringify(summary(isFollowing, followersCount)), {
      headers: { "Content-Type": "application/json" },
    }),
  );
}

describe("profile summary cache key", () => {
  afterEach(() => vi.restoreAllMocks());

  it("is the string the read path actually fetches", () => {
    // The one-line statement of the defect: the key the reader subscribes to and the URL
    // the reader fetches have to be the same string, because the key IS the URL.
    const key = profileSummaryKey(CREATOR, VIEWER);
    const fetchSpy = stubSummaryFetch();

    void profileApi.summary(CREATOR, VIEWER);

    expect(fetchSpy).toHaveBeenCalledWith(key, expect.anything());
  });

  it("separates viewers, and never sends the label to the server", () => {
    expect(profileSummaryKey(CREATOR, "a")).not.toBe(profileSummaryKey(CREATOR, "b"));
    expect(profileSummaryKey(CREATOR, null)).not.toBe(profileSummaryKey(CREATOR, "a"));

    // The key doubles as the request URL, so a `?viewer=` partition would reach the API
    // as an unbound parameter. A fragment keeps the partition client-side.
    const fetchSpy = stubSummaryFetch();
    void profileApi.summary(CREATOR, VIEWER);
    expect(fetchSpy.mock.calls[0][0] as string).not.toContain("?viewer=");
  });
});

describe("follow updates the rendered summary without a reload", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    mockedAuth.mockReturnValue(signedIn(VIEWER));
  });

  afterEach(() => vi.restoreAllMocks());

  it("flips isFollowing and the follower count in the reader's own slot", async () => {
    stubSummaryFetch();
    const followPost = vi.spyOn(api, "request").mockResolvedValue({
      isFollowing: true,
      followersCount: 11,
    });

    const reader = renderHook(() => useProfileSummary(CREATOR, VIEWER), { wrapper });
    await waitFor(() => expect(reader.result.current.summary).toBeTruthy());
    expect(reader.result.current.summary?.isFollowing).toBe(false);
    expect(reader.result.current.summary?.followersCount).toBe(10);

    const follower = renderHook(() => useFollowProfile(CREATOR), { wrapper });

    await act(async () => {
      await follower.result.current.follow();
    });

    expect(followPost).toHaveBeenCalledTimes(1);

    // The reader sees the new state with no refetch and no remount. On the broken code
    // this is where it fails: the reader still reports isFollowing false.
    await waitFor(() => expect(reader.result.current.summary?.isFollowing).toBe(true));
    expect(reader.result.current.summary?.followersCount).toBe(11);

    // Unfollow returns it through the same slot, so the control round-trips.
    vi.mocked(api.request).mockResolvedValue({
      isFollowing: false,
      followersCount: 10,
    });

    await act(async () => {
      await follower.result.current.unfollow();
    });

    await waitFor(() => expect(reader.result.current.summary?.isFollowing).toBe(false));
    expect(reader.result.current.summary?.followersCount).toBe(10);
  });

  it("leaves another viewer's cached copy untouched", async () => {
    const other = "viewer-2";

    stubSummaryFetch();
    vi.spyOn(api, "request").mockResolvedValue({
      isFollowing: true,
      followersCount: 11,
    });

    const mine = renderHook(() => useProfileSummary(CREATOR, VIEWER), { wrapper });
    const theirs = renderHook(() => useProfileSummary(CREATOR, other), { wrapper });

    await waitFor(() => expect(mine.result.current.summary).toBeTruthy());
    await waitFor(() => expect(theirs.result.current.summary).toBeTruthy());

    const follower = renderHook(() => useFollowProfile(CREATOR), { wrapper });
    await act(async () => {
      await follower.result.current.follow();
    });

    await waitFor(() => expect(mine.result.current.summary?.isFollowing).toBe(true));

    // The other reader's copy must not have been told anything: `isFollowing` is a fact
    // about who asked, and this is the leak the partition exists to prevent.
    expect(theirs.result.current.summary?.isFollowing).toBe(false);
    expect(theirs.result.current.summary?.followersCount).toBe(10);
  });
});
