import { Badge, type BadgeTone } from "@/components/ui/badge";
import type { UserRole } from "@/types/admin";

/**
 * The role badge, in one place because it now renders on six surfaces (the admin user
 * table, its confirmation dialogs, the account page, event cards, the event detail page
 * and the public profile) and all of them must agree on which colour means privilege and
 * on what each role is called.
 *
 * Admin is `brand` rather than `warn` deliberately: being an Admin is not a warning
 * about the account, and a colour reserved elsewhere for destructive state would
 * make an ordinary row read as a problem.
 *
 * `LABELS` is where "Creator" lives. The backend role is `Moderator` - the capability
 * that publishes public events without review, which is what a club or society account
 * actually is - and the product calls that tier Creator. The two names are not the same
 * thing and this map is the only place allowed to know that, so the wording cannot
 * drift between a card, a profile and the admin table. Anything that persists or
 * compares a role must keep using `Moderator`; only display goes through here.
 */
const tones: Record<UserRole, BadgeTone> = {
  Admin: "brand",
  Moderator: "info",
  Member: "neutral",
};

const LABELS: Record<UserRole, string> = {
  Admin: "Admin",
  Moderator: "Creator",
  Member: "Member",
};

/** The star is the Creator mark specifically, and only here. Applied to Admin too it
 * would say "this account is privileged" on the two roles where that is not the
 * message, and the point of the badge is that a club reads as established rather
 * than as staff. */
const MARKS: Partial<Record<UserRole, string>> = {
  Moderator: "\u{2B50}",
};

export function RoleBadge({
  role,
  className,
}: {
  /** Both null and undefined mean "not known", and both render nothing. The profile
   * payload types this as `UserRole | null` because that is what an absent-or-null
   * JSON field deserialises to, so accepting only undefined would push a
   * `?? undefined` into every caller. */
  role: UserRole | null | undefined;
  className?: string;
}) {
  // An unknown role renders nothing rather than guessing "Member": during a session
  // restore the value is genuinely not known yet, and a badge is a claim.
  if (!role) return null;
  const mark = MARKS[role];
  return (
    <Badge tone={tones[role]} className={className}>
      {mark ? `${mark} ${LABELS[role]}` : LABELS[role]}
    </Badge>
  );
}

/** The Creator presentation for surfaces that build their own header rather than
 * drop in the badge - currently the profile page, where the spec asks for the
 * creator identity to read as more prominent than a badge in a row. Exported so the
 * star-and-word pair cannot be reassembled differently there. */
export function isCreatorRole(role: UserRole | null | undefined): boolean {
  return role === "Moderator";
}
