import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { JoinButton, deriveJoinState } from "@/components/event/join-button";

/**
 * The approval branch of the join state machine.
 *
 * The rule these hold: a pending request is the member's own standing and outranks
 * every capacity fact, while an *open* event must derive exactly the states it did
 * before this feature existed — that equivalence is the backward-compatibility
 * guarantee, and it is checked here rather than assumed.
 */
const OPEN = {
  isHost: false,
  isCancelled: false,
  isJoined: false,
  isFull: false,
  secondsUntilStart: 3600,
};

describe("deriveJoinState — open events are unchanged", () => {
  it("derives the states it did before join approval existed", () => {
    expect(deriveJoinState(OPEN)).toBe("idle");
    expect(deriveJoinState({ ...OPEN, isFull: true })).toBe("full");
    expect(deriveJoinState({ ...OPEN, isJoined: true })).toBe("joined");
    expect(deriveJoinState({ ...OPEN, isHost: true })).toBe("idle");
    expect(deriveJoinState({ ...OPEN, isCancelled: true })).toBe("idle");
    expect(deriveJoinState({ ...OPEN, secondsUntilStart: 30 })).toBe("starting");
  });

  it("ignores an absent isRequestPending, so callers that predate it are unaffected", () => {
    expect(deriveJoinState({ ...OPEN, isRequestPending: undefined })).toBe("idle");
  });
});

describe("deriveJoinState — gated events", () => {
  it("reports a submitted request as pending when a request is waiting", () => {
    expect(deriveJoinState({ ...OPEN, isRequestPending: true })).toBe("pending");
  });

  it("outranks full: 'you have already asked' beats a capacity fact", () => {
    expect(deriveJoinState({ ...OPEN, isRequestPending: true, isFull: true })).toBe(
      "pending",
    );
  });

  it("yields to starting, which is terminal and nearer than any decision", () => {
    expect(
      deriveJoinState({ ...OPEN, isRequestPending: true, secondsUntilStart: 10 }),
    ).toBe("starting");
  });

  it("yields to host and cancelled, which are not the requester's situation", () => {
    expect(deriveJoinState({ ...OPEN, isHost: true, isRequestPending: true })).toBe(
      "idle",
    );
    expect(
      deriveJoinState({ ...OPEN, isCancelled: true, isRequestPending: true }),
    ).toBe("idle");
  });
});

describe("JoinButton — approval copy", () => {
  it("labels the idle control as a request when the host approves members", () => {
    render(
      <JoinButton
        state="idle"
        current={2}
        max={10}
        requiresApproval
        onJoin={() => {}}
      />,
    );
    expect(screen.getByRole("button", { name: /request to join/i })).toBeTruthy();
    expect(screen.getByText(/host approves who joins/i)).toBeTruthy();
  });

  it("keeps the plain Join label when approval is off", () => {
    render(<JoinButton state="idle" current={2} max={10} onJoin={() => {}} />);
    expect(screen.getByRole("button", { name: /^join$/i })).toBeTruthy();
    expect(screen.queryByText(/host approves who joins/i)).toBeNull();
  });

  it("shows a disabled Request submitted rather than a live control", () => {
    render(
      <JoinButton state="pending" current={2} max={10} requiresApproval onJoin={() => {}} />,
    );
    const button = screen.getByRole("button", { name: /request submitted/i });
    expect(button.hasAttribute("disabled")).toBe(true);
    expect(screen.getByText(/host will review your request/i)).toBeTruthy();
  });

  it("still says Full over the approval hint when the event is full and ungated", () => {
    render(<JoinButton state="full" current={10} max={10} onJoin={() => {}} />);
    expect(screen.getByText(/just filled up/i)).toBeTruthy();
  });
});
