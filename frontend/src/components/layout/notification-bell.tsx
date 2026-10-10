"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ApiError } from "@/lib/api";
import { listNotifications, markAllNotificationsRead, markNotificationRead } from "@/lib/notifications";
import { toast } from "@/components/ui/toast";
import type { NotificationItem } from "@/types/notifications";

export function NotificationBell({ enabled }: { enabled: boolean }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const inbox = await listNotifications();
      setItems(inbox.items);
      setUnreadCount(inbox.unreadCount);
    } catch (error) {
      toast(error instanceof ApiError ? error.message : "Could not load notifications.", "info");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!enabled) return;
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [enabled, load]);

  async function markRead(id: string) {
    try {
      await markNotificationRead(id);
      setItems((current) => current.map((item) => item.id === id ? { ...item, read: true } : item));
      setUnreadCount((count) => Math.max(0, count - 1));
    } catch (error) {
      toast(error instanceof ApiError ? error.message : "Could not mark notification as read.", "info");
    }
  }

  async function markAllRead() {
    try {
      await markAllNotificationsRead();
      setItems((current) => current.map((item) => ({ ...item, read: true })));
      setUnreadCount(0);
    } catch (error) {
      toast(error instanceof ApiError ? error.message : "Could not mark notifications as read.", "info");
    }
  }

  async function openNotification(item: NotificationItem) {
    if (!item.read) await markRead(item.id);
    setOpen(false);
    if (item.link) router.push(item.link);
  }

  if (!enabled) return null;

  return (
    <div className="relative">
      <button
        type="button"
        aria-label={unreadCount ? `Notifications, ${unreadCount} unread` : "Notifications"}
        aria-expanded={open}
        aria-controls="notification-inbox"
        onClick={() => {
          const nextOpen = !open;
          setOpen(nextOpen);
          if (nextOpen) void load();
        }}
        className="press relative inline-flex h-9 w-9 items-center justify-center rounded-md border border-border bg-surface text-fg hover:bg-surface-2"
      >
        <svg aria-hidden="true" viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
          <path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9" />
          <path d="M10 21h4" />
        </svg>
        {unreadCount > 0 && (
          <span aria-hidden="true" className="absolute -right-1 -top-1 min-w-4 rounded-full bg-danger px-1 text-[10px] leading-4 text-white">
            {unreadCount > 99 ? "99+" : unreadCount}
          </span>
        )}
      </button>

      {open && (
        <section
          id="notification-inbox"
          aria-label="Notification inbox"
          className="absolute right-0 z-40 mt-2 max-h-[min(70vh,28rem)] w-[min(22rem,calc(100vw-2rem))] overflow-y-auto rounded-lg border border-border bg-surface p-3 shadow-float"
        >
          <div className="mb-2 flex items-center justify-between gap-3">
            <h2 className="text-h3 text-fg">Notifications</h2>
            <div className="flex items-center gap-1">
              {unreadCount > 0 && (
                <button type="button" onClick={() => void markAllRead()} className="rounded px-2 py-1 text-micro text-brand-600 hover:bg-brand-tint">
                  Mark all as read
                </button>
              )}
              <button type="button" onClick={() => setOpen(false)} className="rounded px-2 py-1 text-meta text-fg-muted hover:bg-surface-2">
                Close
              </button>
            </div>
          </div>
          {loading ? (
            <p className="py-6 text-center text-meta text-fg-muted" role="status">Loading notifications…</p>
          ) : items.length === 0 ? (
            <p className="py-6 text-center text-meta text-fg-muted">You&rsquo;re all caught up.</p>
          ) : (
            <ul className="flex flex-col divide-y divide-border">
              {items.map((item) => (
                <li key={item.id} className="flex gap-2 py-3">
                  <div className="min-w-0 flex-1">
                    {item.link ? (
                      <button
                        type="button"
                        onClick={() => void openNotification(item)}
                        className={`block w-full text-left text-meta hover:underline ${item.read ? "text-fg-muted" : "font-semibold text-fg"}`}
                      >
                        <span className="block text-micro uppercase tracking-wide text-fg-muted">{item.title}</span>
                        {item.message}
                      </button>
                    ) : (
                      <p className={`text-meta ${item.read ? "text-fg-muted" : "font-semibold text-fg"}`}>
                        <span className="block text-micro uppercase tracking-wide text-fg-muted">{item.title}</span>
                        {item.message}
                      </p>
                    )}
                    <time className="mt-1 block text-micro text-fg-muted" dateTime={item.createdAt}>
                      {formatNotificationDate(item.createdAt)}
                    </time>
                  </div>
                  {!item.read && (
                    <button
                      type="button"
                      onClick={() => void markRead(item.id)}
                      className="shrink-0 self-start rounded px-2 py-1 text-micro text-brand-600 hover:bg-brand-tint"
                    >
                      Mark read
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </div>
  );
}

function formatNotificationDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "Recently"
    : new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
}
