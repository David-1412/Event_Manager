import { Badge, type BadgeTone } from "@/components/ui/badge";
import type { UserRole } from "@/types/admin";

/**
 * The role badge, in one place because three surfaces render it (the admin user
 * table, its confirmation dialogs, and the account page) and all three must agree
 * on which colour means privilege.
 *
 * Admin is `brand` rather than `warn` deliberately: being an Admin is not a warning
 * about the account, and a colour reserved elsewhere for destructive state would
 * make an ordinary row read as a problem.
 */
const tones: Record<UserRole, BadgeTone> = {
  Admin: "brand",
  Moderator: "info",
  Member: "neutral",
};

export function RoleBadge({
  role,
  className,
}: {
  role: UserRole | undefined;
  className?: string;
}) {
  // An unknown role renders nothing rather than guessing "Member": during a session
  // restore the value is genuinely not known yet, and a badge is a claim.
  if (!role) return null;
  return <Badge tone={tones[role]} className={className}>{role}</Badge>;
}
