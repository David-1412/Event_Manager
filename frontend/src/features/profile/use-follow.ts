"use client";

import { useCallback, useState } from "react";
import { useSWRConfig } from "swr";
import { ApiError, request } from "@/lib/api";
import { useAuth } from "@/lib/auth/auth-provider";
import { profileSummaryKey } from "./profile-api";
import type { ProfileFollowResult } from "@/types/profile";

/**
 * Following one profile, and the control's state while it settles.
 *
 * The write goes through `request` rather than SWR because it is a mutation, matching
 * `useToggleInterest`. What differs is what happens on success: rather than invalidating
 * a set the page reads, it writes the server's answer straight into the summary cache
 * with `revalidate: false`. The response already carries both values the header shows,
 * and refetching here would replace the number the server just computed with a second,
 * slightly later one — visible as the follower count ticking up twice.
 *
 * The endpoints are idempotent, so `follow` and `unfollow` are safe to retry after a
 * dropped response. That matters because a follower count is exactly the kind of number
 * a user taps twice when nothing appears to happen.
 */
export function useFollowProfile(id: string) {
  const { user, loading } = useAuth();
  const { mutate } = useSWRConfig();
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  // Resolved once here so both the guard below and the cache key above read the same
  // value. `user.id` and not `user.uid`: the follow endpoint keys on our `users.id`,
  // and the Firebase uid is a different identifier that would compare unequal to every
  // profile, including the reader's own.
  const viewerId = user?.id;

  const run = useCallback(
    async (method: "POST" | "DELETE"): Promise<boolean> => {
      if (!id) return false;
      setFailure(null);
      setPending(true);
      try {
        const result = await request<ProfileFollowResult>(`/api/profiles/${id}/follow`, {
          method,
        });
        await mutate(
          profileSummaryKey(id, viewerId),
          (current) =>
            current
              ? {
                  ...current,
                  isFollowing: result.isFollowing,
                  followersCount: result.followersCount,
                }
              : current,
          { revalidate: false },
        );
        return true;
      } catch (error) {
        // The server's own wording where it has one. A self-follow and a
        // not-signed-in call both arrive as a 400 whose `detail` is the sentence the
        // service chose, and rephrasing it here would put a second, vaguer copy
        // between the reader and the reason the tap did nothing.
        setFailure(
          error instanceof ApiError
            ? error.detail || error.message || "Could not update the follow."
            : "Could not update the follow.",
        );
        return false;
      } finally {
        setPending(false);
      }
    },
    // viewerId, not user?.id. The cache partition being written has to be the one the
    // page is reading, and a closure over a stale session would write the result into
    // the previous account's copy of the profile — leaving the button you just tapped
    // showing the old state.
    [id, mutate, viewerId],
  );

  /** True only once a session has resolved *and* it belongs to someone other than
   * the profile's subject. Two different reasons a follow control must not appear —
   * no identity, and following yourself — collapsed into the one flag the header
   * needs, so the page does not repeat the comparison. */
  const canFollow = !loading && !!viewerId && viewerId !== id;

  return {
    canFollow,
    follow: () => run("POST"),
    unfollow: () => run("DELETE"),
    isPending: pending,
    failure,
  };
}
