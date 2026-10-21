"use client";

import Link from "next/link";
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { useSWRConfig } from "swr";
import { AlertCircle, ArrowLeft, Lock, Search, Send } from "lucide-react";
import { NotificationPanel, NotificationTabs, isNotificationTab, type NotificationTab } from "@/components/messages/notification-center";
import { HostLink } from "@/components/profile/host-link";
import { EmptyState } from "@/components/state/empty-error";
import { Avatar } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { isMessagesKey, markConversationRead, sendMessage } from "@/features/messages/messages-api";
import { useConversation, useConversations, useThread } from "@/features/messages/use-messages";
import { ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { cn } from "@/lib/cn";
import { formatTime, formatTimeAgo } from "@/lib/format";
import { useSearchParamsValue } from "@/lib/query";
import type { Conversation } from "@/types/messages";

/** Mirrors MessagingService.ContentMaxLength. */
const MESSAGE_MAX = 2000;

/** A message on screen before (or instead of) the server confirming it. */
interface PendingMessage {
  tempId: string;
  content: string;
  createdAt: string;
  status: "sending" | "sent" | "failed";
  error?: string;
  /** The stored id once the send succeeds; the bubble hides when that copy shows. */
  serverId?: string;
}

/** Why a send failed, in the server's words when it gave some. */
export function describeSendError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return "you're sending too quickly";
    if (error.status === 0) return "no connection";
    return error.errors.detail?.[0] || error.detail || "something went wrong";
  }
  return "something went wrong";
}

/** Switch what the main pane shows - a conversation (`?c=`), a notification tab
 * (`?n=`) or neither - in place: the URL is the state, as on every list page. */
function navigateCenter(search: string) {
  window.history.pushState(null, "", search ? `/messages?${search}` : "/messages");
  window.dispatchEvent(new PopStateEvent("popstate"));
}

function selectConversation(id: string | null) {
  navigateCenter(id ? `c=${encodeURIComponent(id)}` : "");
}

function selectTab(tab: NotificationTab) {
  navigateCenter(`n=${tab}`);
}

/** Conversations whose other person's name contains the search, ignoring case. */
export function filterConversations(conversations: Conversation[], search: string): Conversation[] {
  const q = search.trim().toLocaleLowerCase();
  return q ? conversations.filter((c) => c.other.name.toLocaleLowerCase().includes(q)) : conversations;
}

/**
 * `/messages`: the notification and message center.
 *
 * The sidebar is, top to bottom: the conversation search, the Comments / Likes /
 * Events / Social tabs, the "Messages" header and the conversation list - the first
 * two fixed, the list scrolling on its own. The main pane shows whichever the
 * sidebar selected: a conversation (`?c=`) or one notification tab (`?n=`). A phone
 * shows one pane at a time, with a back arrow to the sidebar. `?draft=` pre-fills
 * the composer when arriving from a "Message" button.
 */
export function MessagesView() {
  const { user, loading } = useAuth();
  const params = useSearchParamsValue();
  const selected = params.get("c");
  const tabParam = params.get("n");
  const tab = !selected && isNotificationTab(tabParam) ? tabParam : null;
  const draft = params.get("draft") ?? "";
  const { conversations, error, isLoading, retry } = useConversations();
  const [search, setSearch] = useState("");
  const shown = conversations ? filterConversations(conversations, search) : undefined;
  const mainOpen = Boolean(selected || tab);

  if (loading) {
    return (
      <Shell>
        <SkeletonBlock className="m-4 h-64" />
      </Shell>
    );
  }

  if (!user) {
    return (
      <Shell>
        <EmptyState
          className="p-6"
          title="Sign in to see your messages."
          action={
            <Link href={signInReturnTo("/messages")} className="no-underline">
              <Button>Sign in</Button>
            </Link>
          }
        />
      </Shell>
    );
  }

  return (
    <Shell>
      <aside
        aria-label="Conversations"
        className={cn(
          "flex min-h-0 w-full flex-col border-border md:w-80 md:shrink-0 md:border-r",
          mainOpen && "hidden md:flex",
        )}
      >
        {/* Fixed at the top of the sidebar: only the conversation list scrolls. */}
        <div className="flex shrink-0 flex-col gap-3 border-b border-border p-3">
          <div role="search" className="relative">
            <label htmlFor="conversation-search" className="sr-only">
              Search conversations
            </label>
            <Search
              aria-hidden
              size={16}
              className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-fg-muted"
            />
            <input
              id="conversation-search"
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search conversations..."
              className="h-10 w-full rounded-full border border-border bg-surface-2 pl-10 pr-4 text-meta text-fg placeholder:text-fg-muted transition-colors focus:border-brand-600 focus:bg-surface focus:outline-none"
            />
          </div>
          <NotificationTabs active={tab} onSelect={selectTab} />
        </div>
        <h1 className="shrink-0 border-b border-border px-4 py-2.5 text-micro font-semibold uppercase tracking-wide text-fg-muted">
          Messages
        </h1>
        <div className="min-h-0 flex-1 overflow-y-auto">
          {error ? (
            <div className="p-4 text-meta text-fg-muted">
              Couldn&apos;t load your messages.{" "}
              <button type="button" onClick={retry} className="font-medium text-brand-600">
                Try again
              </button>
            </div>
          ) : isLoading || !conversations ? (
            <div className="flex flex-col gap-2 p-3">
              {[0, 1, 2].map((i) => (
                <SkeletonBlock key={i} className="h-14 w-full" />
              ))}
            </div>
          ) : conversations.length === 0 ? (
            <p className="p-4 text-meta text-fg-muted">
              No messages yet. Use &ldquo;Message&rdquo; on an event, a forum post or a profile to start a
              conversation.
            </p>
          ) : shown?.length === 0 ? (
            <p className="p-4 text-meta text-fg-muted">No conversations match &ldquo;{search.trim()}&rdquo;.</p>
          ) : (
            <ul>
              {shown?.map((c) => (
                <InboxRow key={c.id} conversation={c} active={c.id === selected} />
              ))}
            </ul>
          )}
        </div>
      </aside>

      <section
        aria-label={tab ? "Notifications" : "Conversation"}
        className={cn("min-h-0 min-w-0 flex-1 flex-col", mainOpen ? "flex" : "hidden md:flex")}
      >
        {selected ? (
          <Thread key={selected} id={selected} initialDraft={draft} />
        ) : tab ? (
          <NotificationPanel key={tab} tab={tab} onBack={() => navigateCenter("")} />
        ) : (
          <div className="flex flex-1 items-center justify-center p-6 text-meta text-fg-muted">
            Choose a conversation or a notification tab.
          </div>
        )}
      </section>
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="mx-auto flex h-[calc(100dvh-3.5rem)] w-full max-w-5xl md:px-4 md:py-4">
      <div className="flex min-h-0 w-full overflow-hidden border-border bg-surface md:rounded-md md:border">
        {children}
      </div>
    </div>
  );
}

function InboxRow({ conversation: c, active }: { conversation: Conversation; active: boolean }) {
  const unread = c.unreadCount > 0;
  return (
    <li>
      <button
        type="button"
        onClick={() => selectConversation(c.id)}
        aria-current={active ? "true" : undefined}
        className={cn(
          "press flex w-full items-center gap-3 border-b border-border px-4 py-3 text-left hover:bg-surface-2",
          active && "bg-surface-2",
        )}
      >
        <Avatar name={c.other.name} src={c.other.photoUrl} size="md" />
        <span className="min-w-0 flex-1">
          <span className="flex items-baseline gap-2">
            <span className={cn("truncate text-body text-fg", unread && "font-semibold")}>{c.other.name}</span>
            {c.lastMessageAt && (
              <span className="ml-auto shrink-0 text-micro text-fg-muted">{formatTimeAgo(c.lastMessageAt)}</span>
            )}
          </span>
          <span className="flex items-center gap-2">
            <span className={cn("truncate text-meta", unread ? "text-fg" : "text-fg-muted")}>
              {c.lastMessageIsMine && "You: "}
              {c.lastMessagePreview}
            </span>
            {unread && (
              <span className="ml-auto shrink-0 rounded-full bg-danger px-1.5 text-[11px] font-bold leading-[18px] text-white tabular-nums">
                {c.unreadCount}
                <span className="sr-only"> unread</span>
              </span>
            )}
          </span>
        </span>
      </button>
    </li>
  );
}

function Thread({ id, initialDraft }: { id: string; initialDraft: string }) {
  const { user } = useAuth();
  const { mutate } = useSWRConfig();
  const { conversation, error: conversationError } = useConversation(id);
  const thread = useThread(id);
  const [text, setText] = useState(initialDraft);
  const [pending, setPending] = useState<PendingMessage[]>([]);
  const scroller = useRef<HTMLDivElement>(null);
  const lastCount = useRef(0);

  // A pending message that the server has confirmed is shown by its stored copy
  // (appended to the thread cache), so it is hidden here once that copy is on screen
  // - and only then, so there is never a frame with neither.
  const confirmedIds = new Set(thread.messages?.map((m) => m.id));
  const visiblePending = pending.filter((p) => !p.serverId || !confirmedIds.has(p.serverId));
  useEffect(() => {
    if (pending.some((p) => p.serverId && confirmedIds.has(p.serverId))) {
      queueMicrotask(() =>
        setPending((list) => list.filter((p) => !p.serverId || !confirmedIds.has(p.serverId))),
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [thread.messages, pending]);

  // Keep the newest message in view as messages arrive or are sent, but not when the
  // reader has loaded older history and is scrolled up reading it.
  const shownCount = (thread.messages?.length ?? 0) + visiblePending.length;
  useLayoutEffect(() => {
    const el = scroller.current;
    if (!el || shownCount === 0) return;
    const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 120;
    if (lastCount.current === 0 || nearBottom || shownCount > lastCount.current) el.scrollTop = el.scrollHeight;
    lastCount.current = shownCount;
  }, [shownCount]);

  // Opening the thread, or a message arriving while it is open, marks it read.
  const newest = thread.messages?.[thread.messages.length - 1];
  useEffect(() => {
    if (!newest || !user?.id || newest.senderId === user.id) return;
    void markConversationRead(id)
      .then(() => mutate(isMessagesKey))
      .catch(() => undefined);
  }, [id, newest?.id, newest?.senderId, user?.id, mutate, newest]);

  /**
   * Send without waiting: the message is on screen and the box is clear before the
   * request leaves. The response's stored copy then replaces it in place, and the
   * inbox and unread count refresh in the background - nothing on screen waits on
   * those refetches any more, which is what made sending feel like ~3 seconds.
   */
  function deliver(item: PendingMessage) {
    setPending((list) =>
      list.map((p) => (p.tempId === item.tempId ? { ...p, status: "sending", error: undefined } : p)),
    );
    sendMessage(id, item.content)
      .then((stored) => {
        thread.appendMessage(stored);
        setPending((list) =>
          list.map((p) => (p.tempId === item.tempId ? { ...p, status: "sent", serverId: stored.id } : p)),
        );
        void mutate(isMessagesKey); // inbox order, previews and badges, in the background
      })
      .catch((error: unknown) => {
        setPending((list) =>
          list.map((p) => (p.tempId === item.tempId ? { ...p, status: "failed", error: describeSendError(error) } : p)),
        );
      });
  }

  function send() {
    const content = text.trim();
    if (!content) return;
    const item: PendingMessage = {
      tempId: `temp-${Date.now()}-${Math.random().toString(36).slice(2)}`,
      content,
      createdAt: new Date().toISOString(),
      status: "sending",
    };
    setPending((list) => [...list, item]);
    setText("");
    deliver(item);
  }

  function discard(tempId: string) {
    setPending((list) => list.filter((p) => p.tempId !== tempId));
  }

  if (conversationError) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 p-6 text-center">
        <p className="text-body text-fg">This conversation isn&apos;t available.</p>
        <Button variant="secondary" onClick={() => selectConversation(null)}>
          Back to messages
        </Button>
      </div>
    );
  }

  const other = conversation?.other;

  return (
    <>
      <header className="flex items-center gap-3 border-b border-border px-3 py-2.5">
        <button
          type="button"
          onClick={() => selectConversation(null)}
          aria-label="Back to conversations"
          className="press flex h-9 w-9 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 md:hidden"
        >
          <ArrowLeft aria-hidden size={18} />
        </button>
        {other ? (
          <HostLink userId={other.id} name={other.name} avatarUrl={other.photoUrl} role={other.role} size="md" withBadge />
        ) : (
          <SkeletonBlock className="h-8 w-40" />
        )}
      </header>

      <div ref={scroller} className="min-h-0 flex-1 overflow-y-auto px-3 py-4" aria-live="polite" aria-relevant="additions">
        {thread.hasOlder && (
          <div className="mb-3 flex justify-center">
            <Button variant="ghost" size="sm" onClick={thread.loadOlder} loading={thread.isLoadingOlder}>
              Load older messages
            </Button>
          </div>
        )}
        {thread.isLoading && !thread.messages ? (
          <div className="flex flex-col gap-2">
            <SkeletonBlock className="h-10 w-2/3" />
            <SkeletonBlock className="ml-auto h-10 w-1/2" />
          </div>
        ) : thread.messages?.length === 0 && visiblePending.length === 0 ? (
          <p className="flex items-center justify-center gap-1.5 py-8 text-center text-meta text-fg-muted">
            <Lock aria-hidden size={14} />
            Only you and {other?.name ?? "them"} can see this conversation.
          </p>
        ) : (
          <ol className="flex flex-col gap-1.5">
            {thread.messages?.map((m) => {
              const mine = m.senderId === user?.id;
              return (
                <li key={m.id} className={cn("flex", mine ? "justify-end" : "justify-start")}>
                  <div
                    className={cn(
                      "max-w-[80%] rounded-2xl px-3.5 py-2",
                      mine ? "rounded-br-sm bg-brand-600 text-brand-fg" : "rounded-bl-sm bg-surface-2 text-fg",
                    )}
                  >
                    <p className="whitespace-pre-wrap break-words text-body">{m.content}</p>
                    <time
                      dateTime={m.createdAt}
                      className={cn("mt-0.5 block text-right text-[11px]", mine ? "opacity-80" : "text-fg-muted")}
                    >
                      {formatTime(m.createdAt)}
                    </time>
                  </div>
                </li>
              );
            })}
            {visiblePending.map((p) => (
              <li key={p.tempId} className="flex flex-col items-end" data-pending={p.status}>
                <div
                  className={cn(
                    "max-w-[80%] rounded-2xl rounded-br-sm px-3.5 py-2",
                    p.status === "failed"
                      ? "border border-danger bg-danger-tint text-fg"
                      : cn("bg-brand-600 text-brand-fg", p.status === "sending" && "opacity-70"),
                  )}
                >
                  <p className="whitespace-pre-wrap break-words text-body">{p.content}</p>
                  <span className="mt-0.5 block text-right text-[11px] opacity-80">
                    {p.status === "sending" ? "Sending…" : formatTime(p.createdAt)}
                  </span>
                </div>
                {p.status === "failed" && (
                  <p role="alert" className="mt-1 flex items-center gap-2 text-micro text-danger">
                    <AlertCircle aria-hidden size={13} />
                    <span>Not sent{p.error ? ` · ${p.error}` : ""}</span>
                    <button type="button" onClick={() => deliver(p)} className="font-semibold underline">
                      Retry
                    </button>
                    <button type="button" onClick={() => discard(p.tempId)} className="text-fg-muted underline">
                      Remove
                    </button>
                  </p>
                )}
              </li>
            ))}
          </ol>
        )}
      </div>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          void send();
        }}
        className="border-t border-border p-3"
      >
        <div className="flex items-end gap-2">
          <label htmlFor={`composer-${id}`} className="sr-only">
            Message {other?.name}
          </label>
          <textarea
            id={`composer-${id}`}
            value={text}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              // Enter sends, Shift+Enter is a new line - the chat convention.
              if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
                e.preventDefault();
                void send();
              }
            }}
            rows={1}
            maxLength={MESSAGE_MAX}
            placeholder="Write a message"
            className="max-h-36 min-h-11 flex-1 resize-none rounded-2xl border border-border bg-surface-2 px-4 py-2.5 text-body text-fg placeholder:text-fg-muted focus:border-brand-600 focus:bg-surface focus:outline-none"
          />
          <button
            type="submit"
            disabled={!text.trim()}
            aria-label="Send"
            className="press flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-brand-600 text-brand-fg hover:bg-brand-700 disabled:bg-surface-2 disabled:text-fg-muted"
          >
            <Send aria-hidden size={18} />
          </button>
        </div>
        {text.length > MESSAGE_MAX - 200 && (
          <p className="mt-1 text-right text-micro text-fg-muted tabular-nums">
            {text.length}/{MESSAGE_MAX}
          </p>
        )}
      </form>
    </>
  );
}
