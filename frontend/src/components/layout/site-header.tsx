"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Avatar } from "@/components/ui/avatar";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { NotificationBell } from "@/components/layout/notification-bell";
import { useAuth } from "@/lib/auth/auth-provider";
import { cn } from "@/lib/cn";

const links = [
  { href: "/", label: "Browse" },
  { href: "/my-events", label: "My events" },
  { href: "/create", label: "Create" },
] as const;

/**
 * The admin destination, rendered only when the API has confirmed the role.
 *
 * Hidden rather than shown-and-refused: requirement is that a Member cannot *see*
 * the admin page, and the backend's 404-for-non-admins is built on the same idea —
 * the surface should be indistinguishable from absent. `isAdmin` is false while the
 * role is still loading, so the link appears a beat after the session restores
 * rather than flashing at a Member.
 */
const adminLinks = [
  { href: "/admin/users", label: "Admin users" },
  { href: "/admin/events", label: "Manage events" },
] as const;

/**
 * The header is a client component now that it renders the session. The mark
 * and nav markup are unchanged, so server rendering still produces them —
 * `AuthProvider` resolves on the client and only the account control changes
 * after hydration.
 *
 * `aria-current="page"` replaces what a server-side active-link helper would
 * have done: `usePathname` gives the same answer one commit later, and the
 * nav's three destinations are not worth a server component boundary.
 */
export function SiteHeader() {
  const pathname = usePathname();
  const { isAdmin, user } = useAuth();
  const visibleLinks = isAdmin ? [...links, ...adminLinks] : links;
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface shadow-raise">
      <div className="mx-auto flex h-14 max-w-[1400px] items-center gap-4 px-4">
        <Link
          href="/"
          className="flex items-center gap-2 text-h3 text-fg no-underline"
          aria-label="MonaHub home"
        >
          <span aria-hidden className="text-brand-600">
            ◆
          </span>
          MonaHub
        </Link>

        <nav aria-label="Primary" className="flex items-center gap-1 overflow-x-auto">
          {visibleLinks.map((l) => (
            <Link
              key={l.href}
              href={l.href}
              aria-current={pathname === l.href ? "page" : undefined}
              className={cn(
                "press rounded-md px-3 py-2 text-meta font-medium hover:bg-surface-2 hover:text-fg",
                pathname === l.href ? "bg-surface-2 text-fg" : "text-fg-muted",
              )}
            >
              {l.label}
            </Link>
          ))}
        </nav>

        <div className="ml-auto flex items-center gap-2">
          <ThemeToggle />
          <NotificationBell enabled={Boolean(user)} />
          <AccountControl />
        </div>
      </div>
    </header>
  );
}

/**
 * Sign-in link, or the signed-in identity. Keeps the same footprint in both
 * states so the header can't shift when a session restores (spec §11), and the
 * control is a link to `/account` rather than a dropdown: with two destinations
 * a menu is one extra tap and a focus-trap to get wrong.
 */
function AccountControl() {
  const { user, loading } = useAuth();

  // Same box as both alternatives while the session restores, so the header
  // never renders a "Sign in" link to a logged-in user and then swaps it.
  if (loading)
    return <span aria-hidden className="hidden h-9 w-[92px] animate-pulse rounded-md bg-surface-2 sm:inline-block" />;

  if (!user)
    return (
      <Link
        href="/login"
        className="press hidden h-9 items-center rounded-md border border-border bg-surface px-4 text-meta font-medium text-fg hover:bg-surface-2 sm:inline-flex"
      >
        Sign in
      </Link>
    );

  const firstName = user.displayName.split(" ")[0] || user.displayName;
  return (
    <Link
      href="/account"
      className="press inline-flex h-9 items-center gap-2 rounded-md border border-border bg-surface pl-1.5 pr-3 text-meta font-medium text-fg hover:bg-surface-2"
      aria-label={`Account — ${user.displayName}`}
    >
      <Avatar name={user.displayName} src={user.photoURL} size="sm" />
      <span className="hidden max-w-[10ch] truncate sm:inline">{firstName}</span>
    </Link>
  );
}

/** Sticky footer bar for the single primary CTA (spec §6). */
export function StickyFooter({ children }: { children: React.ReactNode }) {
  return (
    <div className="sticky bottom-0 z-20 border-t border-border bg-surface p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-raise">
      <div className="mx-auto flex max-w-[680px] gap-2 lg:max-w-none">{children}</div>
    </div>
  );
}
