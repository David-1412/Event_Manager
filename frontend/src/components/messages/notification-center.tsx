"use client";

import { useRouter } from "next/navigation";
import { ArrowLeft, Bell, CalendarDays, Heart, MessageSquare, UserPlus, type LucideIcon } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useSWRConfig } from "swr";
import { Avatar } from "@/components/ui/avatar";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { isActivityKey, markActivityRead } from "@/features/messages/messages-api";
import { useActivity, useEventNotifications, useNotificationCounts } from "@/features/messages/use-messages";
import { cn } from "@/lib/cn";
import { formatTimeAgo } from "@/lib/format";
import { markAllNotificationsRead } from "@/lib/notifications";
import type { ActivityKind } from "@/types/messages";

/** The four notification tabs above the inbox. */
export type NotificationTab = "comments" | "likes" | "events" | "social";

export const NOTIFICATION_TABS: { value: NotificationTab; label: string; Icon: LucideIcon; iconClass: string }[] = [
  { value: "comments", label: "Comments", Icon: MessageSquare, iconClass: "text-brand-600" },
  { value: "likes", label: "Likes", Icon: Heart, iconClass: "fill-red-500 text-red-500" },
  { value: "events", label: "Events", Icon: CalendarDays, iconClass: "text-brand-600" },
  { value: "social", label: "Social", Icon: UserPlus, iconClass: "text-brand-600" },
];

export const isNotificationTab = (value: string | null): value is NotificationTab =>
  NOTIFICATION_TABS.some((t) => t.value === value);

/** Social is the follows feed; Comments and Likes map straight onto their activity feeds. */
const ACTIVITY_KIND: Record<Exclude<NotificationTab, "events">, ActivityKind> = {
  comments: "comments",
  likes: "likes",
  social: "follows",
};

/**
 * The tab row: Comments | Likes | Events | Social, each with its unread count.
 *
 * Pills that scroll sideways on a phone; on desktop the four share the sidebar width
 * and drop their labels to the icon and badge, so the section stays one compact row.
 * The label is still the button's accessible name at every width.
 */
export function NotificationTabs({
  active,
  onSelect,
}: {
  active: NotificationTab | null;
  onSelect: (tab: NotificationTab) => void;
}) {
  const counts = useNotificationCounts();

  return (
    <nav aria-label="Notifications" className="-mx-1 flex gap-1.5 overflow-x-auto px-1 md:grid md:grid-cols-4">
      {NOTIFICATION_TABS.map(({ value, label, Icon, iconClass }) => {
        const unread = counts[value];
        const selected = active === value;
        return (
          <button
            key={value}
            type="button"
            onClick={() => onSelect(value)}
            aria-current={selected ? "page" : undefined}
            aria-label={unread > 0 ? `${label}, ${unread} unread` : label}
            title={label}
            className={cn(
              "press relative inline-flex h-9 shrink-0 items-center justify-center gap-1.5 rounded-full border px-3 text-meta font-medium transition-colors md:px-2",
              selected
                ? "border-brand-600 bg-brand-tint text-brand-600"
                : "border-border bg-surface text-fg-muted hover:bg-surface-2 hover:text-fg",
            )}
          >
            <Icon aria-hidden size={16} className={iconClass} />
            <span className="md:sr-only">{label}</span>
            {unread > 0 && (
              <span
                aria-hidden
                className="min-w-[18px] rounded-full bg-danger px-1 text-center text-[11px] font-bold leading-[18px] text-white tabular-nums"
              >
                {unread > 99 ? "99+" : unread}
              </span>
            )}
          </button>
        );
      })}
    </nav>
  );
}

/**
 * One tab's notifications, in the main pane where a conversation would open.
 * Opening the tab marks everything in it read (see useReadAllOnOpen); what was new
 * stays tinted while you look, and a row opens what it is about.
 */
export function NotificationPanel({ tab, onBack }: { tab: NotificationTab; onBack: () => void }) {
  const { label } = NOTIFICATION_TABS.find((t) => t.value === tab)!;

  return (
    <>
      <header className="flex items-center gap-2 border-b border-border px-3 py-2.5">
        <button
          type="button"
          onClick={onBack}
          aria-label="Back to messages"
          className="press flex h-9 w-9 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 md:hidden"
        >
          <ArrowLeft aria-hidden size={18} />
        </button>
        <h2 className="text-h3 text-fg">{label}</h2>
      </header>
      {tab === "events" ? <EventNotificationList /> : <ActivityList kind={ACTIVITY_KIND[tab]} label={label} />}
    </>
  );
}

const EMPTY: Record<ActivityKind, string> = {
  comments: "No comments yet. When someone comments on your posts or replies to you, it shows here.",
  likes: "No likes yet. When someone marks your posts or events Interested, it shows here.",
  follows: "No new followers yet.",
};

/**
 * Opening a tab reads everything in it: the moment its items arrive, every unread one
 * is marked read on the server and the tab's badge clears. Which ones were new is
 * remembered for as long as the tab stays open, so they keep their highlight and
 * "New" dot - the reader can still see what arrived since last time.
 */
function useReadAllOnOpen<T>(
  items: T[] | undefined,
  isUnread: (item: T) => boolean,
  keyOf: (item: T) => string,
  markAll: (keys: string[]) => Promise<void>,
): ReadonlySet<string> | null {
  const [fresh, setFresh] = useState<ReadonlySet<string> | null>(null);
  const done = useRef(false);

  useEffect(() => {
    if (!items || done.current) return;
    done.current = true;
    const unread = items.filter(isUnread).map(keyOf);
    queueMicrotask(() => setFresh(new Set(unread)));
    if (unread.length > 0) void markAll(unread);
    // Once per opening of the tab (the panel is keyed by tab, so it remounts).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items]);

  return fresh;
}

function ActivityList({ kind, label }: { kind: ActivityKind; label: string }) {
  const router = useRouter();
  const { mutate } = useSWRConfig();
  const { items, error, isLoading, mutate: mutateList } = useActivity(kind);

  const fresh = useReadAllOnOpen(
    items,
    (i) => !i.isRead,
    (i) => i.key,
    async (keys) => {
      await mutateList((list) => list?.map((i) => (keys.includes(i.key) ? { ...i, isRead: true } : i)), {
        revalidate: false,
      });
      try {
        // The API takes up to 100 keys per call; a tab holds at most 50, but chunk anyway.
        for (let i = 0; i < keys.length; i += 100) await markActivityRead(keys.slice(i, i + 100));
      } catch {
        // Not fatal: the items come back as unread next time, and nothing is lost.
      } finally {
        void mutate(isActivityKey); // the badge counts
      }
    },
  );

  return (
    <PanelBody
      error={error}
      loading={isLoading && !items}
      empty={items?.length === 0 ? EMPTY[kind] : null}
      label={label}
    >
      {items?.map((item) => {
        const isNew = fresh ? fresh.has(item.key) : !item.isRead;
        return (
          <li key={item.key}>
            <button
              type="button"
              onClick={() => router.push(item.href)}
              className={cn(
                "press flex w-full items-start gap-3 border-b border-border px-4 py-3 text-left transition-colors hover:bg-surface-2",
                isNew && "bg-brand-tint/60",
              )}
            >
              <Avatar name={item.actor?.name ?? "Anonymous"} src={item.actor?.photoUrl} size="md" />
              <span className="min-w-0 flex-1">
                <span className="block text-body text-fg">
                  <span className="font-semibold">{item.actor?.name ?? "Anonymous"}</span> {item.action}
                  {item.targetTitle && <span className="text-fg-muted"> &ldquo;{item.targetTitle}&rdquo;</span>}
                </span>
                {item.snippet && <span className="mt-0.5 line-clamp-2 block text-meta text-fg-muted">{item.snippet}</span>}
                <time dateTime={item.createdAt} className="mt-0.5 block text-micro text-fg-muted">
                  {formatTimeAgo(item.createdAt)}
                </time>
              </span>
              {isNew && <NewDot />}
            </button>
          </li>
        );
      })}
    </PanelBody>
  );
}

function EventNotificationList() {
  const router = useRouter();
  const { list, error, isLoading, mutate } = useEventNotifications();
  const items = list?.items;

  const fresh = useReadAllOnOpen(
    items,
    (i) => !i.read,
    (i) => i.id,
    async () => {
      await mutate(
        (current) => current && { items: current.items.map((i) => ({ ...i, read: true })), unreadCount: 0 },
        { revalidate: false },
      );
      await markAllNotificationsRead().catch(() => undefined);
      void mutate();
    },
  );

  return (
    <PanelBody
      error={error}
      loading={isLoading && !list}
      empty={items?.length === 0 ? "No event notifications yet. Reminders and new events from people you follow show here." : null}
      label="Events"
    >
      {items?.map((item) => {
        const isNew = fresh ? fresh.has(item.id) : !item.read;
        return (
          <li key={item.id}>
            <button
              type="button"
              onClick={() => item.link && router.push(item.link)}
              className={cn(
                "press flex w-full items-start gap-3 border-b border-border px-4 py-3 text-left transition-colors hover:bg-surface-2",
                isNew && "bg-brand-tint/60",
              )}
            >
              <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-tint text-brand-600">
                <Bell aria-hidden size={18} />
              </span>
              <span className="min-w-0 flex-1">
                <span className="block text-body font-semibold text-fg">{item.title}</span>
                <span className="mt-0.5 block text-meta text-fg-muted">{item.message}</span>
                <time dateTime={item.createdAt} className="mt-0.5 block text-micro text-fg-muted">
                  {formatTimeAgo(item.createdAt)}
                </time>
              </span>
              {isNew && <NewDot />}
            </button>
          </li>
        );
      })}
    </PanelBody>
  );
}

function PanelBody({
  error,
  loading,
  empty,
  label,
  children,
}: {
  error: unknown;
  loading: boolean;
  empty: string | null;
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      {error ? (
        <p className="p-4 text-meta text-fg-muted">Couldn&apos;t load {label.toLowerCase()}.</p>
      ) : loading ? (
        <div className="flex flex-col gap-2 p-3" aria-busy>
          {[0, 1, 2, 3].map((i) => (
            <SkeletonBlock key={i} className="h-14 w-full" />
          ))}
        </div>
      ) : empty ? (
        <p className="p-6 text-center text-meta text-fg-muted">{empty}</p>
      ) : (
        <ul aria-label={label}>{children}</ul>
      )}
    </div>
  );
}

/** Marks what arrived since the tab was last opened. */
function NewDot() {
  return (
    <span className="mt-2 h-2.5 w-2.5 shrink-0 rounded-full bg-brand-600">
      <span className="sr-only">New</span>
    </span>
  );
}

/** Where a tab's notifications live, for links into the center from elsewhere. */
export const notificationHref = (tab: NotificationTab) => `/messages?n=${tab}`;
