"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState, useSyncExternalStore } from "react";
import { useTheme } from "next-themes";
import {
  CalendarCheck,
  CalendarPlus,
  ChevronDown,
  LogOut,
  Moon,
  Settings,
  Shield,
  Sun,
  User,
  Users,
} from "lucide-react";
import { Avatar } from "@/components/ui/avatar";
import { HeaderSearch } from "@/components/layout/global-search";
import { Floating } from "@/components/ui/card";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { useNotificationCounts, useUnreadCount } from "@/features/messages/use-messages";

/**
 * Everything waiting in the Messages center: unread messages plus unread Comments,
 * Likes, Events and Social notifications. With the bell gone, this one number is how
 * the header says "something happened".
 */
function useCenterUnread(): number {
  return useUnreadCount() + useNotificationCounts().total;
}
import { useAuth } from "@/lib/auth/auth-provider";
import { cn } from "@/lib/cn";

/** The global sections. Everything personal or administrative lives in the account
 * menu, and everything section-specific (Following, My Events, Create) lives in the
 * section's own sub-navigation, so this row stays two words long. */
const links = [
  { href: "/", label: "Events" },
  { href: "/forum", label: "Forum" },
  { href: "/messages", label: "Messages" },
] as const;

/** A section stays highlighted on its sub-pages (`/forum/abc` lights "Forum"), but
 * `/` matches only itself - otherwise "Events" would light on every page. Events also
 * owns its own sub-pages that do not sit under `/`: event details, My Events, Create
 * and the search results the Events chip leads to. */
export function isActiveLink(pathname: string | null, href: string): boolean {
  if (!pathname) return false;
  if (href === "/") {
    return (
      pathname === "/" ||
      ["/events", "/my-events", "/create", "/drafts"].some((p) => pathname === p || pathname.startsWith(`${p}/`))
    );
  }
  return pathname === href || pathname.startsWith(`${href}/`);
}

/**
 * The global navbar: brand and Events | Forum | Messages on the left, the search in
 * the middle, the account menu on the right.
 * Messages is the one place unread things are counted (messages and notifications
 * together); there is no separate bell or chat icon to disagree with it.
 */
export function SiteHeader() {
  const pathname = usePathname();
  const unread = useCenterUnread();

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface shadow-raise">
      <div className="mx-auto flex h-14 max-w-[1400px] items-center gap-2 px-4 sm:gap-4">
        <Link href="/" className="flex items-center gap-2 text-h3 text-fg no-underline" aria-label="MonaHub home">
          <span aria-hidden className="text-brand-600">
            ◆
          </span>
          {/* The mark alone on the narrowest phones, so Events and Forum and all
              three icons still fit on one line. */}
          <span className="hidden min-[400px]:inline">MonaHub</span>
        </Link>

        <nav aria-label="Primary" className="flex items-center gap-1">
          {links.map((l) => {
            const active = isActiveLink(pathname, l.href);
            const badge = l.href === "/messages" && unread > 0 ? unread : 0;
            return (
              <Link
                key={l.href}
                href={l.href}
                aria-current={active ? "page" : undefined}
                aria-label={badge ? `${l.label}, ${badge} unread` : undefined}
                className={cn(
                  "press inline-flex items-center gap-1.5 rounded-md px-2 py-2 text-meta font-semibold no-underline hover:bg-surface-2 hover:text-fg sm:px-3",
                  active ? "bg-surface-2 text-fg" : "text-fg-muted",
                )}
              >
                {l.label}
                {badge > 0 && <UnreadBadge count={badge} />}
              </Link>
            );
          })}
        </nav>

        {/* Center: the search, inline from lg; below that its magnifying glass sits
            at the right, beside the account menu. */}
        <div className="flex min-w-0 flex-1 items-center justify-end lg:justify-center">
          <HeaderSearch />
        </div>

        <div className="flex items-center gap-1 sm:gap-2">
          <AccountMenu />
        </div>
      </div>
    </header>
  );
}


/** The red unread count used on both the Messages link and the chat icon. Its
 * number is decorative here; the link's accessible name carries the count. */
function UnreadBadge({ count, className }: { count: number; className?: string }) {
  return (
    <span
      aria-hidden
      className={cn(
        "min-w-[18px] rounded-full bg-danger px-1 text-center text-[11px] font-bold leading-[18px] text-white tabular-nums",
        className,
      )}
    >
      {count > 99 ? "99+" : count}
    </span>
  );
}

const subscribeNoop = () => () => {};

/**
 * Signed out: "Sign in" (plus the theme switch, which signed-in people find in the
 * menu). Signed in: the avatar, opening a menu with the personal and admin links that
 * used to crowd the navbar.
 *
 * A disclosure menu: Escape and a click outside close it, focus moves to the first
 * item on open and back to the avatar on close, and the admin entries only render
 * once the API has confirmed the role.
 */
function AccountMenu() {
  const { user, loading, isAdmin, logout } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const { resolvedTheme, setTheme } = useTheme();
  const mounted = useSyncExternalStore(subscribeNoop, () => true, () => false);
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuId = useId();

  // Any navigation closes the menu.
  useEffect(() => {
    queueMicrotask(() => setOpen(false));
  }, [pathname]);

  useEffect(() => {
    if (!open) return;
    menuRef.current?.querySelector<HTMLElement>("[role=menuitem]")?.focus();
    const onDown = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  // The same footprint while the session restores, so the header never shows a
  // "Sign in" link to someone who is signed in and then swaps it.
  if (loading) return <span aria-hidden className="h-9 w-9 animate-pulse rounded-full bg-surface-2 sm:w-[92px]" />;

  if (!user)
    return (
      <>
        <ThemeToggle />
        <Link
          href="/login"
          className="press inline-flex h-9 items-center rounded-md border border-border bg-surface px-3 text-meta font-medium text-fg no-underline hover:bg-surface-2 sm:px-4"
        >
          Sign in
        </Link>
      </>
    );

  const isDark = mounted && resolvedTheme === "dark";
  const item =
    "flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-meta text-fg no-underline hover:bg-surface-2 focus:bg-surface-2 focus:outline-none";

  return (
    <div ref={rootRef} className="relative">
      <button
        ref={triggerRef}
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={menuId}
        aria-label={`Account menu for ${user.displayName}`}
        onClick={() => setOpen((o) => !o)}
        className="press inline-flex h-9 items-center gap-1 rounded-full border border-border bg-surface p-0.5 pr-1.5 hover:bg-surface-2"
      >
        <Avatar name={user.displayName} src={user.photoURL} size="sm" />
        <ChevronDown aria-hidden size={14} className="text-fg-muted" />
      </button>

      {open && (
        <Floating className="absolute right-0 top-11 z-40 w-64 p-1.5">
          <div ref={menuRef} id={menuId} role="menu" aria-label="Account">
            <div className="border-b border-border px-3 pb-2 pt-1.5">
              <p className="truncate text-meta font-semibold text-fg">{user.displayName}</p>
              {user.email && <p className="truncate text-micro text-fg-muted">{user.email}</p>}
            </div>

            <div className="py-1">
              {user.id && (
                <Link role="menuitem" href={`/profile/${user.id}`} className={item}>
                  <User aria-hidden size={16} /> Profile
                </Link>
              )}
              <Link role="menuitem" href="/my-events" className={item}>
                <CalendarCheck aria-hidden size={16} /> My events
              </Link>
              <Link role="menuitem" href="/create" className={item}>
                <CalendarPlus aria-hidden size={16} /> Create event
              </Link>
              <Link role="menuitem" href="/account" className={item}>
                <Settings aria-hidden size={16} /> Account
              </Link>
            </div>

            {isAdmin && (
              <div className="border-t border-border py-1">
                <p className="px-3 pb-1 pt-1.5 text-micro uppercase text-fg-muted">Admin</p>
                <Link role="menuitem" href="/admin/users" className={item}>
                  <Users aria-hidden size={16} /> Admin users
                </Link>
                <Link role="menuitem" href="/admin/events" className={item}>
                  <Shield aria-hidden size={16} /> Manage events
                </Link>
              </div>
            )}

            <div className="border-t border-border pt-1">
              <button
                role="menuitem"
                type="button"
                className={item}
                onClick={() => setTheme(isDark ? "light" : "dark")}
              >
                {isDark ? <Sun aria-hidden size={16} /> : <Moon aria-hidden size={16} />}
                {isDark ? "Light mode" : "Dark mode"}
              </button>
              <button
                role="menuitem"
                type="button"
                className={item}
                onClick={() => {
                  setOpen(false);
                  void logout().then(() => router.push("/"));
                }}
              >
                <LogOut aria-hidden size={16} /> Sign out
              </button>
            </div>
          </div>
        </Floating>
      )}
    </div>
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
