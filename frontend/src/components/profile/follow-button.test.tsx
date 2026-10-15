import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FollowButton } from "@/components/profile/follow-button";
import { useAuth } from "@/lib/auth/auth-provider";
import { useFollowProfile } from "@/features/profile/use-follow";

/**
 * The follow control's visibility rules — the part of the feature that is easy to get
 * wrong and hard to see.
 *
 * The button has four states and only two of them are the interesting ones. A
 * signed-out visitor must still be *offered* the button (tapping signs them in and
 * brings them back), while the profile's own subject must see nothing at all. The
 * second one is the failure mode worth a test: a control that appears on your own
 * profile is a button that can never do anything, and it is invisible to anyone
 * testing while logged out.
 *
 * Auth and navigation are stubbed rather than driven through Firebase or the App
 * Router — what is under test is which state the component renders, not whether the
 * provider resolves.
 */

const push = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
  usePathname: () => "/profile/creator-1",
}));
vi.mock("@/lib/auth/auth-provider");
vi.mock("@/features/profile/use-follow");

const mockedUseAuth = vi.mocked(useAuth);
const mockedUseFollow = vi.mocked(useFollowProfile);

/** A signed-in identity. Built through a factory rather than written inline because
 * the component only reads `id`, while the type demands the whole record — spelling
 * out the unused fields at every call site would bury the one that matters. */
function signedIn(id: string): ReturnType<typeof useAuth> {
  return {
    user: {
      uid: `firebase-${id}`,
      id,
      displayName: "Test Viewer",
      email: "viewer@example.com",
      photoURL: null,
      emailVerified: true,
    },
    loading: false,
  } as ReturnType<typeof useAuth>;
}

function auth(partial: Partial<ReturnType<typeof useAuth>>) {
  mockedUseAuth.mockReturnValue(partial as ReturnType<typeof useAuth>);
}

function follow(partial: Partial<ReturnType<typeof useFollowProfile>> = {}) {
  mockedUseFollow.mockReturnValue({
    canFollow: false,
    follow: vi.fn().mockResolvedValue(undefined),
    unfollow: vi.fn().mockResolvedValue(undefined),
    isPending: false,
    failure: null,
    ...partial,
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  auth({ user: null, loading: false });
  follow();
});

describe("FollowButton", () => {
  it("offers a signed-out visitor the button rather than hiding the action", () => {
    render(<FollowButton userId="creator-1" isFollowing={false} />);
    expect(screen.getByRole("button", { name: "Follow" })).toBeInTheDocument();
  });

  it("renders nothing on the viewer's own profile", () => {
    auth(signedIn("me"));
    mockedUseFollow.mockReturnValue({
      canFollow: false,
      follow: vi.fn(),
      unfollow: vi.fn(),
      isPending: false,
      failure: null,
    });

    const { container } = render(<FollowButton userId="me" isFollowing={false} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("shows Following when the server says the caller follows", () => {
    auth(signedIn("viewer"));
    follow({ canFollow: true });

    render(<FollowButton userId="creator-1" isFollowing />);
    const button = screen.getByRole("button", { name: "Following" });
    expect(button).toHaveAttribute("aria-pressed", "true");
  });

  it("does not flash a signed-out control while the session is still resolving", () => {
    // Firebase reports its first state a frame or two after mount. Treating `loading`
    // as signed-out would show "Follow" and then swap it, and would let a click in the
    // gap send a logged-in reader off to the sign-in page.
    auth({ ...signedIn("viewer"), loading: true });

    render(<FollowButton userId="creator-1" isFollowing />);
    expect(screen.getByRole("button", { name: "Following" })).toBeInTheDocument();
  });

  it("routes a signed-out tap to sign-in instead of calling the API", async () => {
    const followFn = vi.fn();
    follow({ canFollow: false, follow: followFn });

    render(<FollowButton userId="creator-1" isFollowing={false} />);
    await userEvent.click(screen.getByRole("button", { name: "Follow" }));

    // Sent to sign-in and back to this profile, and the endpoint never called: a
    // request without a token would come back 401 and read as a broken button.
    expect(followFn).not.toHaveBeenCalled();
    expect(push).toHaveBeenCalledWith("/login?next=%2Fprofile%2Fcreator-1");
  });

  it("calls unfollow when an existing follow is tapped", async () => {
    auth(signedIn("viewer"));
    const unfollowFn = vi.fn().mockResolvedValue(undefined);
    follow({ canFollow: true, unfollow: unfollowFn });

    render(<FollowButton userId="creator-1" isFollowing />);
    await userEvent.click(screen.getByRole("button", { name: "Following" }));

    expect(unfollowFn).toHaveBeenCalledTimes(1);
  });

  it("surfaces the server's reason beside the button rather than as a toast", () => {
    follow({ failure: "You cannot follow yourself." });

    render(<FollowButton userId="creator-1" isFollowing={false} />);
    expect(screen.getByRole("alert")).toHaveTextContent("You cannot follow yourself.");
  });

  it("disables the control while a write is in flight", () => {
    follow({ isPending: true });

    render(<FollowButton userId="creator-1" isFollowing={false} />);
    expect(screen.getByRole("button", { name: "Follow" })).toBeDisabled();
  });
});
