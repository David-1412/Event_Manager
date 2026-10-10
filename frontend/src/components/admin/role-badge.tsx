import { Badge, type BadgeTone } from "@/components/ui/badge";
import type { UserRole } from "@/types/admin";

/**
 * The role badge, in one place because several surfaces render it (the admin user
 * table and the account page) and all of them must agree on which colour means
 * which role.
 *
 * Admin is `brand` rather than `warn` deliberately: being an Admin is not a warning
 * about the account, and a colour reserved elsewhere for destructive state would
 * make an ordinary row read as a problem. Creator is `info` — distinct from Member
 * at a glance, and quieter than Admin because it carries no authority over others.
 */
const tones: Record<UserRole, BadgeTone> = {
  Admin: "brand",
  Creator: "info",
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
