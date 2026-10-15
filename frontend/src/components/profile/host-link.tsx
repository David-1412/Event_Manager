import Link from "next/link";
import { Avatar } from "@/components/ui/avatar";
import { RoleBadge } from "@/components/admin/role-badge";
import { cn } from "@/lib/cn";
import type { UserRole } from "@/types/admin";

/** The profile route for a host. One function so the URL shape exists in exactly
 * one place: when an organization gets a slug, or the route moves, this is the only
 * line that changes and every host link in the app follows it. */
export function profileHref(userId: string): string {
  return `/profile/${userId}`;
}

/**
 * A host, rendered as a link to their public profile.
 *
 * Every surface that names a host - browse card, preview panel, event detail, the
 * "Hosted by" block - goes through this component so "click the host, see who they
 * are" behaves identically everywhere rather than being implemented four times and
 * drifting.
 *
 * Degrades to plain text when there is no id. `hostId` is optional on the wire for
 * exactly this reason: a response written before the field existed, or a fixture
 * that never carried one, should render an unlinked name rather than a link to
 * `/profile/undefined`, which is a real route that would 404.
 */
export function HostLink({
  userId,
  name,
  avatarUrl,
  role,
  size = "sm",
  withBadge = false,
  className,
}: {
  userId: string | null | undefined;
  name: string | null | undefined;
  avatarUrl?: string | null;
  /** The host's role, when the payload carries it. Drives the Creator badge. */
  role?: UserRole | null;
  size?: "sm" | "md" | "lg";
  /** Renders the role badge beside the name. Off by default: a dense card has no
   * room for it, and the surfaces that want it (detail page, profile) opt in. */
  withBadge?: boolean;
  className?: string;
}) {
  const label = name?.trim();
  if (!label) return null;

  const body = (
    <>
      <Avatar name={label} src={avatarUrl} size={size} />
      <span className="min-w-0 truncate">{label}</span>
      {withBadge && <RoleBadge role={role} />}
    </>
  );

  if (!userId) {
    return (
      <span className={cn("inline-flex min-w-0 items-center gap-2", className)}>
        {body}
      </span>
    );
  }

  return (
    <Link
      href={profileHref(userId)}
      className={cn(
        "inline-flex min-w-0 items-center gap-2 rounded-sm text-fg hover:text-brand-600 hover:underline",
        className,
      )}
    >
      {body}
    </Link>
  );
}
