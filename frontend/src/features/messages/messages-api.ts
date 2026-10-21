import { API_BASE_URL, request } from "@/lib/api";
import type { ActivityKind, Conversation, DirectMessage, ForumComment } from "@/types/messages";

/**
 * Keys and writes for direct messages and forum comments.
 *
 * Message keys carry the viewer in a `#` fragment (never sent - the server takes
 * the caller from the token), so one account's inbox can never be served from the
 * cache to the next person who signs in on the same browser.
 */
const viewer = (viewerId?: string | null) => (viewerId ? `#viewer=${viewerId}` : "");

export const conversationsKey = (viewerId?: string | null) =>
  `${API_BASE_URL}/api/messages/conversations${viewer(viewerId)}`;

export const conversationKey = (id: string, viewerId?: string | null) =>
  `${API_BASE_URL}/api/messages/conversations/${id}${viewer(viewerId)}`;

export const messagesKey = (id: string, before: number | null, viewerId?: string | null) =>
  `${API_BASE_URL}/api/messages/conversations/${id}/messages${before ? `?before=${before}` : ""}${viewer(viewerId)}`;

export const unreadCountKey = (viewerId?: string | null) =>
  `${API_BASE_URL}/api/messages/unread-count${viewer(viewerId)}`;

/** Every cached key that depends on the inbox - refreshed together when a message lands. */
export const isMessagesKey = (key: unknown) => typeof key === "string" && key.includes("/api/messages/");

export const startConversation = (recipientUserId: string) =>
  request<Conversation>("/api/messages/conversations", { method: "POST", body: { recipientUserId } });

export const sendMessage = (conversationId: string, content: string) =>
  request<DirectMessage>(`/api/messages/conversations/${conversationId}/messages`, {
    method: "POST",
    body: { content },
  });

export const markConversationRead = (conversationId: string) =>
  request<void>(`/api/messages/conversations/${conversationId}/read`, { method: "POST" });

export const commentsKey = (postId: string, viewerId?: string | null) =>
  `${API_BASE_URL}/api/forum/posts/${postId}/comments${viewer(viewerId)}`;

export const addComment = (
  postId: string,
  body: { content: string; parentCommentId: string | null; isAnonymous: boolean },
) => request<ForumComment>(`/api/forum/posts/${postId}/comments`, { method: "POST", body });

export const deleteComment = (commentId: string) =>
  request<void>(`/api/forum/comments/${commentId}`, { method: "DELETE" });

export const updateComment = (commentId: string, content: string) =>
  request<ForumComment>(`/api/forum/comments/${commentId}`, { method: "PUT", body: { content } });

/** One activity tab's items: likes, follows or comments. */
export const activityKey = (kind: ActivityKind, viewerId?: string | null) =>
  `${API_BASE_URL}/api/notifications/${kind}${viewer(viewerId)}`;

export const activityCountsKey = (viewerId?: string | null) =>
  `${API_BASE_URL}/api/notifications/activity-counts${viewer(viewerId)}`;

/** Every cached key that depends on activity read state. */
export const isActivityKey = (key: unknown) =>
  typeof key === "string" && /\/api\/notifications\/(likes|follows|comments|activity-counts)/.test(key);

export const markActivityRead = (keys: string[]) =>
  request<void>("/api/notifications/activity-read", { method: "POST", body: { keys } });

/** The messages page, opened on one conversation, optionally with a starter draft. */
export const messagesHref = (conversationId: string, draft?: string) =>
  `/messages?c=${encodeURIComponent(conversationId)}${draft ? `&draft=${encodeURIComponent(draft)}` : ""}`;
