"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/cn";
import { spotsTakenSentence } from "@/lib/format";
import type { JoinFailure } from "@/types/events";

/**
 * The state machine from UIUX_DESIGN_SPEC.md §7 — a machine, not a button with
 * a handler:
 *
 *   idle ──click──▶ joining ──2xx──▶ joined
 *                     ├──409──▶ full     (count rolled back + inline message)
 *                     └──5xx/network──▶ error (count restored + `Try again`)
 *   any  ──start<60s──▶ starting (terminal)
 *
 * Rules the implementation is held to:
 *  - the control never disappears: every state renders a button or a labelled
 *    message in the same slot, so nothing vanishes under a tap;
 *  - the optimistic count lives in the parent, which rolls it back on failure
 *    (spec principle 5);
 *  - the spinner replaces the label at held width (`aria-busy`, no layout shift);
 *  - `aria-live="polite"` announces `3 of 4 spots taken`, never a bare `3/4`.
 */

export type JoinState = "idle" | "joining" | "joined" | "full" | "starting" | "error";

export const STARTING_THRESHOLD_SECONDS = 60;

export interface JoinButtonProps {
  state: JoinState;
  current: number;
  max: number;
  isHost?: boolean;
  isCancelled?: boolean;
  /** Set by the parent from the ApiError class; drives the inline message. */
  failure?: JoinFailure | null;
  size?: "sm" | "md" | "lg";
  fullWidth?: boolean;
  onJoin: () => void;
  onLeave?: () => void;
}

export function deriveJoinState(args: {
  isHost: boolean;
  isCancelled: boolean;
  isJoined: boolean;
  isFull: boolean;
  secondsUntilStart: number;
}): JoinState {
  if (args.isHost || args.isCancelled) return "idle";
  if (args.secondsUntilStart < STARTING_THRESHOLD_SECONDS) return "starting";
  if (args.isFull) return "full";
  if (args.isJoined) return "joined";
  return "idle";
}

export function JoinButton({
  state,
  current,
  max,
  isHost = false,
  isCancelled = false,
  failure = null,
  size = "md",
  fullWidth = false,
  onJoin,
  onLeave,
}: JoinButtonProps) {
  const [announce, setAnnounce] = useState("");
  const previous = useRef(current);

  // Announce count changes only — not every render, and never a bare `3/4`.
  useEffect(() => {
    if (previous.current !== current) {
      setAnnounce(spotsTakenSentence(current, max));
      previous.current = current;
    }
  }, [current, max]);

  return (
    <div className={cn("flex flex-col items-stretch gap-1", fullWidth && "w-full")}>
      <Control
        state={state}
        current={current}
        max={max}
        isHost={isHost}
        isCancelled={isCancelled}
        size={size}
        fullWidth={fullWidth}
        onJoin={onJoin}
        onLeave={onLeave}
      />
      {/* Same slot, same text style: the rollback message never reflows the
          card or the sticky footer. */}
      <p
        className={cn(
          "min-h-4 text-meta",
          state === "full" || failure === "full" ? "text-danger" : "text-fg-muted",
          (state === "error" || failure === "server" || failure === "network") &&
            "text-danger",
        )}
      >
        {messageFor({ state, failure, current, max, isHost, isCancelled })}
      </p>
      <span aria-live="polite" className="sr-only">
        {announce}
      </span>
    </div>
  );
}


function Control({
  state,
  current,
  max,
  isHost,
  isCancelled,
  size,
  fullWidth,
  onJoin,
  onLeave,
}: {
  state: JoinState;
  current: number;
  max: number;
  isHost: boolean;
  isCancelled: boolean;
  size: "sm" | "md" | "lg";
  fullWidth: boolean;
  onJoin: () => void;
  onLeave?: () => void;
}) {
  if (isCancelled) {
    return (
      <Button size={size} fullWidth={fullWidth} disabled>
        Cancelled
      </Button>
    );
  }
  if (isHost) {
    // Hosts can't join their own event; the label says why (spec §8).
    return (
      <Button size={size} fullWidth={fullWidth} disabled>
        You&apos;re hosting
      </Button>
    );
  }

  switch (state) {
    case "joining":
      return (
        <Button size={size} fullWidth={fullWidth} loading>
          Join
        </Button>
      );
    case "joined":
      return (
        <Button
          size={size}
          fullWidth={fullWidth}
          variant="secondary"
          disabled={onLeave === undefined}
          onClick={onLeave}
        >
          Joined ✓
        </Button>
      );
    case "full":
      return (
        <Button size={size} fullWidth={fullWidth} disabled>
          Full
        </Button>
      );
    case "starting":
      return (
        <Button size={size} fullWidth={fullWidth} disabled>
          Starting
        </Button>
      );
    case "error":
      return (
        <Button size={size} fullWidth={fullWidth} variant="secondary" onClick={onJoin}>
          Try again
        </Button>
      );
    case "idle":
    default:
      return (
        <Button size={size} fullWidth={fullWidth} onClick={onJoin}>
          {max - current === 1 ? "Take the last spot" : "Join"}
        </Button>
      );
  }
}

function messageFor({
  state,
  failure,
  current,
  max,
  isHost,
  isCancelled,
}: {
  state: JoinState;
  failure: JoinFailure | null;
  current: number;
  max: number;
  isHost: boolean;
  isCancelled: boolean;
}): string {
  if (isCancelled) return "This event was cancelled by the host.";
  if (isHost) return `${current} of ${max} spots taken.`;
  if (state === "starting") return "This event is about to start.";
  if (failure === "full" || state === "full") return "This event just filled up.";
  if (failure === "network" || failure === "server" || state === "error") {
    return "Couldn’t reach the server — your spot wasn’t taken.";
  }
  if (state === "joined") {
    const left = Math.max(0, max - current);
    return left === 0
      ? "You’re in — that was the last spot."
      : `You’re in — ${left} ${left === 1 ? "spot" : "spots"} left.`;
  }
  return "";
}
