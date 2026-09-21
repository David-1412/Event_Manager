import Link from "next/link";
import { ThemeToggle } from "@/components/layout/theme-toggle";

const links = [
  { href: "/", label: "Browse" },
  { href: "/my-events", label: "My events" },
  { href: "/create", label: "Create" },
] as const;

export function SiteHeader() {
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface shadow-raise">
      <div className="mx-auto flex h-14 max-w-[1400px] items-center gap-4 px-4">
        <Link
          href="/"
          className="flex items-center gap-2 text-h3 text-fg no-underline"
          aria-label="Event Manager home"
        >
          {/* the mark, not an icon: `EVNT` survives a missing emoji font */}
          <span aria-hidden className="text-brand-600">
            ◆
          </span>
          EVNT
        </Link>

        <nav aria-label="Primary" className="flex items-center gap-1 overflow-x-auto">
          {links.map((l) => (
            <Link
              key={l.href}
              href={l.href}
              className="press rounded-md px-3 py-2 text-meta font-medium text-fg-muted hover:bg-surface-2 hover:text-fg"
            >
              {l.label}
            </Link>
          ))}
        </nav>

        <div className="ml-auto flex items-center gap-2">
          <ThemeToggle />
          <Link
            href="/login"
            className="press hidden h-9 items-center rounded-md border border-border bg-surface px-4 text-meta font-medium text-fg hover:bg-surface-2 sm:inline-flex"
          >
            Sign in
          </Link>
        </div>
      </div>
    </header>
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
