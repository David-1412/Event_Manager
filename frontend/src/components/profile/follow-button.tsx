"use client";

import { useRouter } from "next/navigation";
import { usePathname } from "next/navigation";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { useFollowProfile } from "@/features/profile/use-follow";

/**
 * The Follow / Following control on a public profile.
 *
 * A signed-out visitor gets the button rather than nothing. Following is the main
 * action on a Creator's page and the reason most readers arrive with an intent, so
 * hiding it from the people who have not signed in yet would hide it from exactly the
 * audience the feature is trying to convert; tapping it sends them to sign in and back
 * to the profile they were looking at, the same route `InterestButton` takes on the
 * event page.
 *
 * Once signed in, the button reflects the server's `isFollowing` rather than a local
 * guess, and its label changes only when the write comes back. A follower count that
 * moves before the button does, or the other way round, reads as two systems that
 * disagree.
 */
export function FollowButton({
  userId,
  isFollowing,
}: {
  userId: string;
  isFollowing: boolean;
}) {
  const { user, loading } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const { canFollow, follow, unfollow, isPending, failure } = useFollowProfile(userId);

  // The subject's own profile has no control at all: there is nothing to report to
  // yourself, and a "Following" state you could never reach would be a dead button.
  // While the session resolves the button is shown in its signed-out form, since a
  // logged-in reader must not see a flash of "Sign in to follow" they then have to
  // click away from.
  if (!loading && user?.id === userId) return null;

  async function onClick() {
    if (!canFollow) {
      router.push(signInReturnTo(pathname));
      return;
    }

    const wasFollowing = isFollowing;
    const ok = await (wasFollowing ? unfollow() : follow());

    // Reported from the state the click asked for, not from what came back: the hook
    // swallows a failure into `failure` and leaves the count untouched, so a success
    // toast fired on a failed request would describe a follow that never happened.
    // Checked through the returned flag rather than `failure`, which this closure
    // captured before the click and so still reads null after a failed request.
    //
    // Failure is deliberately not toasted. `toast` has no error tone, and the app's
    // rule is that a toast is a transient confirmation while a persistent fact stays on
    // the card — a follow that did not happen is a fact about the button's state, so it
    // is rendered beside it instead of disappearing in three seconds.
    if (!ok) return;
    toast(wasFollowing ? "Unfollowed" : "Following — you'll hear about new events");
  }

  return (
    <div className="flex flex-col items-start gap-1">
      <Button
        variant={isFollowing ? "secondary" : "primary"}
        size="sm"
        onClick={() => void onClick()}
        disabled={isPending}
        aria-pressed={isFollowing}
      >
        {isFollowing ? "Following" : "Follow"}
      </Button>
      {failure && (
        <p role="alert" className="text-meta text-danger">
          {failure}
        </p>
      )}
    </div>
  );
}
