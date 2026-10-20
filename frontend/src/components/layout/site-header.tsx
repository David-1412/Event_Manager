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
  MessageCircle,
  Moon,
  Settings,
  Shield,
  Sun,
  User,
  Users,
} from "lucide-react";
import { Avatar } from "@/components/ui/avatar";
import { Floating } from "@/components/ui/card";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { NotificationBell } from "@/components/layout/notification-bell";
import { useAuth } from "@/lib/auth/auth-provider";
import { cn } from "@/lib/cn";

/** The global sections. Everything personal or administrative lives in the account
 * menu, and everything section-specific (Following, My Events, Create) lives in the
 * section's own sub-navigation, so this row stays two words long. */
const links = [
  { href: "/", label: "Events" },
  { href: "/forum", label: "Forum" },
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
 * The global navbar: brand, Events and Forum, then notifications, messages and the
 * account menu.
 */
export function SiteHeader() {
  const pathname = usePathname();
  const { user } = useAuth();

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
            return (
              <Link
                key={l.href}
                href={l.href}
                aria-current={active ? "page" : undefined}
                className={cn(
                  "press rounded-md px-3 py-2 text-meta font-semibold no-underline hover:bg-surface-2 hover:text-fg",
                  active ? "bg-surface-2 text-fg" : "text-fg-muted",
                )}
              >
                {l.label}
              </Link>
            );
          })}
        </nav>

        <div className="ml-auto flex items-center gap-1 sm:gap-2">
          <NotificationBell enabled={Boolean(user)} />
          <MessagesButton />
          <AccountMenu />
        </div>
      </div>
    </header>
  );
}

/**
 * The chat inbox. MonaHub has no messaging yet, so the icon is shown disabled with a
 * "coming soon" label rather than as a button that opens nothing - a control that
 * looks live and does nothing reads as broken.
 */
function MessagesButton() {
  return (
    <button
      type="button"
      disabled
      aria-label="Messages (coming soon)"
      title="Messages - coming soon"
      className="inline-flex h-9 w-9 cursor-not-allowed items-center justify-center rounded-md text-fg-muted opacity-60"
    >
      <MessageCircle aria-hidden size={20} />
    </button>
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
