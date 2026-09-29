"use client";

import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import { useInterests, setInterestedScope } from "@/features/events/use-interests";
import { useJoinedEvents } from "@/features/events/use-events";
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
  const { isInterested, toggle } = useInterests();
  const { isJoined } = useJoinedEvents();
  const { join, leave, failure, isJoining, isLeaving } = useCardJoin(eventId);

  const interested = isInterested(eventId);
  const joined = isJoined(eventId);

  function onInterest() {
    setInterestedScope(user?.uid ?? null);
    const nowInterested = toggle(eventId);
    toast(nowInterested ? "Marked interested" : "Removed from interested", "success");
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

  async function onLeave() {
    const result = await leave();
    if (result.ok) toast("You've left this event", "success");
  }

  const interestLabel = interested ? "★ Interested" : "☆ Interested";
  const busy = isJoining || isLeaving;

  return (
    <div className={cnRow(fullWidth, busy)}>
      <Button
        size={size}
        variant={interested ? "primary" : "secondary"}
        fullWidth={fullWidth}
        onClick={onInterest}
        aria-pressed={interested}
      >
        {interestLabel}
      </Button>

      {isCancelled ? (
        <Button size={size} variant="secondary" fullWidth={fullWidth} disabled>
          Cancelled
        </Button>
      ) : isHost ? (
        <Button size={size} variant="secondary" fullWidth={fullWidth} disabled>
          You&apos;re hosting
        </Button>
      ) : joined ? (
        <Button size={size} variant="secondary" fullWidth={fullWidth} loading={isLeaving} onClick={() => void onLeave()}>
          Leave event
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

