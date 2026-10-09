"use client";

import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import { useInterests, useToggleInterest } from "@/features/events/use-interests";
import { useJoinedEvents, useIsHosting } from "@/features/events/use-events";
import { useCardJoin } from "@/features/events/use-card-join";

interface CardActionsProps {
  eventId: string;
  /** The detail page knows the hosting/cancelled relation; a browse card does not,
   * so it leaves these false and lets the API reject an illegal join with a
   * "Try again" rather than guessing from a count it does not have. */
  isHost?: boolean;
  isCancelled?: boolean;
  /** "sm" on cards, "md"/"lg" on the detail page. */
  size?: "sm" | "md" | "lg";
  fullWidth?: boolean;
}

/**
 * The Interested + Join/Leave controls, shared by a browse card and the detail
 * page so the two never drift.
 *
 * Interest is client-side (localStorage per uid): Interest ⇄ Uninterest. Join is
 * the real API write via `useCardJoin`: Join ⇄ Leave, with a "Try again" on
 * failure. A detail-page event that is hosting/cancelled shows a disabled reason
 * instead of an action.
 */
export function CardActions({
  eventId,
  isHost = false,
  isCancelled = false,
  size = "sm",
  fullWidth = false,
}: CardActionsProps) {
  const { user } = useAuth();
  const router = useRouter();
  const { isInterested } = useInterests();
  const { isJoined } = useJoinedEvents();
  const { isHosting } = useIsHosting();
  const { join, failure, isJoining, isLeaving } = useCardJoin(eventId);
  const { toggle: toggleInterest, isPending: isToggling } = useToggleInterest(eventId);

  const interested = isInterested(eventId);
  const joined = isJoined(eventId);
  // A browse card cannot see isHost, so derive it from the hosting set; the detail
  // page passes isHost explicitly and that stays authoritative.
  const hosting = isHost || isHosting(eventId);

  // Interest is a signed-in write now (a real event_interests row), so a signed-out
  // tap routes to sign-in like Join does, rather than persisting to this browser.
  async function onInterest() {
    if (!user) {
      requireSignIn();
      return;
    }
    const result = await toggleInterest();
    if (result.ok) toast(result.isInterested ? "Marked interested" : "Removed from interested", "success");
  }

  // Join is the one write a signed-out visitor attempts; send them to sign in and
  // straight back here (the API itself still resolves them to the demo identity).
  function requireSignIn() {
    router.push(`/login?next=${encodeURIComponent(window.location.pathname)}`);
  }

  async function onJoin() {
    if (!user) {
      requireSignIn();
      return;
    }
    const result = await join();
    if (result.ok) toast("You're in", "success");
  }

  const interestLabel = interested ? "★ Interested" : "Interested";
  const busy = isJoining || isLeaving || isToggling;

  return (
    <div className={cnRow(fullWidth, busy)}>
      <Button
        size={size}
        variant={interested ? "primary" : "secondary"}
        fullWidth={fullWidth}
        loading={isToggling}
        onClick={() => void onInterest()}
        aria-pressed={interested}
      >
        {interestLabel}
      </Button>

      {isCancelled ? (
        <Button size={size} variant="secondary" fullWidth={fullWidth} disabled>
          Cancelled
        </Button>
      ) : hosting ? (
        <Button size={size} variant="secondary" fullWidth={fullWidth} disabled>
          You&apos;re hosting
        </Button>
      ) : joined ? (
        <Button size={size} variant="secondary" fullWidth={fullWidth} disabled>
          Joined
        </Button>
      ) : failure && !isJoining ? (
        <Button size={size} variant="secondary" fullWidth={fullWidth} onClick={() => void onJoin()}>
          Try again
        </Button>
      ) : (
        <Button size={size} fullWidth={fullWidth} loading={isJoining} onClick={() => void onJoin()}>
          Join
        </Button>
      )}
    </div>
  );
}

function cnRow(fullWidth: boolean, busy: boolean): string {
  return [
    "flex gap-2",
    fullWidth ? "flex-col" : "flex-wrap items-center",
    busy ? "opacity-90" : "",
  ].join(" ");
}

