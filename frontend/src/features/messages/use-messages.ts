"use client";

import useSWR from "swr";
import useSWRInfinite from "swr/infinite";
import { useAuth } from "@/lib/auth/auth-provider";
import { fetcher } from "@/lib/swr-fetcher";
import type {
  ActivityCounts,
  ActivityItem,
  ActivityKind,
  Conversation,
  DirectMessage,
  ForumComment,
  MessagePage,
} from "@/types/messages";
import {
  activityCountsKey,
  activityKey,
  commentsKey,
  conversationKey,
  conversationsKey,
  messagesKey,
  unreadCountKey,
} from "./messages-api";
import { useMessagesLive } from "./messages-provider";
import { listNotifications } from "@/lib/notifications";
import { API_BASE_URL } from "@/lib/api";

/** Cache key for the event notifications list (viewer-partitioned like the rest). */
export const eventNotificationsKey = (viewerId: string) => `${API_BASE_URL}/api/notifications#viewer=${viewerId}`;

/** Poll interval while the real-time connection is down (blocked WebSockets, a
 * reconnect in progress). Zero while live: pushes trigger the refresh instead. */
const FALLBACK_POLL_MS = 15_000;

function usePoll() {
  return useMessagesLive() ? 0 : FALLBACK_POLL_MS;
}

export function useConversations() {
  const { user } = useAuth();
  const refreshInterval = usePoll();
  const { data, error, isLoading, mutate } = useSWR(
    user?.id ? conversationsKey(user.id) : null,
    (key: string) => fetcher<Conversation[]>(key),
    { refreshInterval, revalidateOnFocus: true },
  );
  return { conversations: data, error, isLoading, retry: () => void mutate() };
}

export function useConversation(id: string | null) {
  const { user } = useAuth();
  const { data, error, isLoading } = useSWR(
    id && user?.id ? conversationKey(id, user.id) : null,
    (key: string) => fetcher<Conversation>(key),
  );
  return { conversation: data, error, isLoading };
}

/**
 * One thread, newest page first in the cache and rendered oldest to newest. "Load
 * older" adds pages before the first; a pushed message revalidates the loaded pages
 * (see MessagesProvider), so the newest page always reflects what is stored.
 */
export function useThread(id: string | null) {
  const { user } = useAuth();
  const refreshInterval = usePoll();
  const { data, error, isLoading, size, setSize, mutate, isValidating } = useSWRInfinite(
    (index: number, previous: MessagePage | null) => {
      if (!id || !user?.id) return null;
      if (index === 0) return messagesKey(id, null, user.id);
      if (!previous?.before) return null;
      return messagesKey(id, previous.before, user.id);
    },
    (key: string) => fetcher<MessagePage>(key),
    { refreshInterval, revalidateFirstPage: true },
  );

  const messages = data ? [...data].reverse().flatMap((page) => page.items) : undefined;
  const hasOlder = Boolean(data?.[data.length - 1]?.before);

  return {
    messages,
    error,
    isLoading,
    hasOlder,
    isLoadingOlder: isValidating && size > (data?.length ?? 0),
    loadOlder: () => void setSize(size + 1),
    refresh: () => void mutate(),
    /**
     * Put a stored message (the send's response) into the newest page straight away,
     * without a refetch. Idempotent by id, so the copy the real-time push brings
     * back moments later does not show twice.
     */
    appendMessage: (message: DirectMessage) =>
      void mutate(
        (pages) => {
          if (!pages || pages.length === 0) return [{ items: [message], before: null }];
          if (pages.some((p) => p.items.some((m) => m.id === message.id))) return pages;
          const [newest, ...older] = pages;
          return [{ ...newest, items: [...newest.items, message] }, ...older];
        },
        { revalidate: false },
      ),
  };
}

export function useUnreadCount() {
  const { user } = useAuth();
  const refreshInterval = usePoll();
  const { data } = useSWR(
    user?.id ? unreadCountKey(user.id) : null,
    (key: string) => fetcher<{ count: number }>(key),
    { refreshInterval, revalidateOnFocus: true },
  );
  return data?.count ?? 0;
}

/** One activity tab. Signed out there is nothing to ask for. */
export function useActivity(kind: ActivityKind | null) {
  const { user } = useAuth();
  const { data, error, isLoading, mutate } = useSWR(
    kind && user?.id ? activityKey(kind, user.id) : null,
    (key: string) => fetcher<ActivityItem[]>(key),
    { revalidateOnFocus: true },
  );
  return { items: data, error, isLoading, mutate };
}

/** Unread counts for the three tab badges, refreshed every minute. */
export function useActivityCounts() {
  const { user } = useAuth();
  const { data } = useSWR(
    user?.id ? activityCountsKey(user.id) : null,
    (key: string) => fetcher<ActivityCounts>(key),
    { refreshInterval: 60_000, revalidateOnFocus: true },
  );
  return data ?? { likes: 0, follows: 0, comments: 0 };
}

/** The event notifications (reminders, new events from people you follow, interest
 * on your events) - what the header bell used to show, now the Events tab. */
export function useEventNotifications() {
  const { user } = useAuth();
  const { data, error, isLoading, mutate } = useSWR(
    user?.id ? eventNotificationsKey(user.id) : null,
    () => listNotifications(),
    { refreshInterval: 60_000, revalidateOnFocus: true },
  );
  return { list: data, error, isLoading, mutate };
}

/** Unread per notification tab, plus their total for the header badge. */
export function useNotificationCounts() {
  const activity = useActivityCounts();
  const { list } = useEventNotifications();
  const counts = {
    comments: activity.comments,
    likes: activity.likes,
    events: list?.unreadCount ?? 0,
    social: activity.follows,
  };
  return { ...counts, total: counts.comments + counts.likes + counts.events + counts.social };
}

export function useComments(postId: string) {
  const { user } = useAuth();
  const { data, error, isLoading, mutate } = useSWR(
    postId ? commentsKey(postId, user?.id) : null,
    (key: string) => fetcher<ForumComment[]>(key),
    { revalidateOnFocus: false },
  );
  return { comments: data, error, isLoading, refresh: () => mutate() };
}
