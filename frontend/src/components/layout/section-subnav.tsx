"use client";

import Link from "next/link";
import { useId, useState } from "react";
import { CalendarPlus, ChevronRight, MessageSquarePlus, Plus } from "lucide-react";
import { Dialog } from "@/components/ui/dialog";
import { cn } from "@/lib/cn";

export interface SubnavItem {
  label: string;
  active: boolean;
  /** A link to another page (e.g. My Events)... */
  href?: string;
  /** ...or an in-page switch (e.g. All / Following on the same feed). */
  onSelect?: () => void;
}

/**
 * The row under the search bar: the section's content tabs on the left, "+ Create"
 * on the right.
 *
 * A `nav` with `aria-current` rather than a tablist, because its items are a mix of
 * in-page switches and links to other pages - and a "tab" that navigates away is not
 * a tab. The row scrolls sideways on a narrow phone instead of wrapping, so the
 * Create button stays on the same line.
 */
export function SectionSubnav({
  items,
  label,
  createFirst = "event",
  className,
}: {
  items: SubnavItem[];
  /** Accessible name, e.g. "Events" or "Forum". */
  label: string;
  /** Which choice the Create dialog lists first - the one this page is about. */
  createFirst?: "event" | "post";
  className?: string;
}) {
  const [open, setOpen] = useState(false);

  return (
    <div className={cn("flex items-center gap-3 border-b border-border", className)}>
      <nav aria-label={label} className="-mb-px flex min-w-0 flex-1 gap-5 overflow-x-auto">
        {items.map((item) => {
          const classes = cn(
            "press inline-flex h-11 shrink-0 items-center border-b-2 text-body font-semibold no-underline",
            item.active ? "border-brand-600 text-fg" : "border-transparent text-fg-muted hover:text-fg",
          );
          return item.href ? (
            <Link key={item.label} href={item.href} aria-current={item.active ? "page" : undefined} className={classes}>
              {item.label}
            </Link>
          ) : (
            <button
              key={item.label}
              type="button"
              aria-current={item.active ? "page" : undefined}
              onClick={item.onSelect}
              className={classes}
            >
              {item.label}
            </button>
          );
        })}
      </nav>

      <button
        type="button"
        onClick={() => setOpen(true)}
        aria-haspopup="dialog"
        className="press mb-1 inline-flex h-9 shrink-0 items-center gap-1.5 rounded-full bg-brand-600 px-4 text-meta font-semibold text-brand-fg shadow-raise hover:bg-brand-700"
      >
        <Plus aria-hidden size={16} strokeWidth={2.5} />
        Create
      </button>

      <CreateDialog open={open} onClose={() => setOpen(false)} first={createFirst} />
    </div>
  );
}

const CHOICES = {
  event: {
    href: "/create",
    title: "Event",
    blurb: "Host a game, meetup or club session people can join.",
    Icon: CalendarPlus,
  },
  post: {
    href: "/forum/new",
    title: "Forum post",
    blurb: "Ask a question, sell something or find a study group.",
    Icon: MessageSquarePlus,
  },
} as const;

/** "What do you want to create?" - an event or a forum post. Each choice opens its
 * own full form; the dialog only routes, so neither form is squeezed into a modal. */
export function CreateDialog({
  open,
  onClose,
  first = "event",
}: {
  open: boolean;
  onClose: () => void;
  first?: "event" | "post";
}) {
  const titleId = useId();
  const order = first === "event" ? (["event", "post"] as const) : (["post", "event"] as const);

  return (
    <Dialog open={open} onClose={onClose} labelledBy={titleId}>
      <h2 id={titleId} className="text-h2 text-fg">
        Create
      </h2>
      <p className="mt-1 text-meta text-fg-muted">What would you like to share?</p>
      <ul className="mt-4 flex flex-col gap-2">
        {order.map((key) => {
          const { href, title, blurb, Icon } = CHOICES[key];
          return (
            <li key={key}>
              <Link
                href={href}
                onClick={onClose}
                className="press flex items-center gap-3 rounded-md border border-border p-4 no-underline hover:border-brand-600 hover:bg-brand-tint"
              >
                <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-tint text-brand-600">
                  <Icon aria-hidden size={20} />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block text-body font-semibold text-fg">{title}</span>
                  <span className="block text-meta text-fg-muted">{blurb}</span>
                </span>
                <ChevronRight aria-hidden size={18} className="shrink-0 text-fg-muted" />
              </Link>
            </li>
          );
        })}
      </ul>
    </Dialog>
  );
}
