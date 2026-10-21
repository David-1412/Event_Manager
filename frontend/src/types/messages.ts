import type { UserRole } from "@/types/admin";

/** Mirrors `ParticipantDto`: the other person in a conversation. */
export interface Participant {
  id: string;
  name: string;
  photoUrl: string | null;
  role: UserRole;
}

/** Mirrors `ConversationDto`. */
export interface Conversation {
  id: string;
  other: Participant;
  lastMessagePreview: string | null;
  lastMessageAt: string | null;
  lastMessageIsMine: boolean;
  unreadCount: number;
}

/** Mirrors `MessageDto`. Whether it is mine is `senderId === user.id`. */
export interface DirectMessage {
  id: string;
  conversationId: string;
  senderId: string;
  content: string;
  createdAt: string;
}

/** Mirrors `MessagePage`: oldest first; `before` fetches the page before it. */
export interface MessagePage {
  items: DirectMessage[];
  before: number | null;
}

/** Mirrors `ForumCommentDto`. */
export interface ForumComment {
  id: string;
  parentCommentId: string | null;
  /** Empty when `isDeleted`. */
  content: string;
  isDeleted: boolean;
  isAnonymous: boolean;
  /** Null when anonymous (unless it is yours or you are an admin) or deleted. */
  author: { id: string; name: string; photoUrl: string | null; role: UserRole } | null;
  isMine: boolean;
  /** Written by the post's author - withheld by the server wherever it would unmask them. */
  isOp: boolean;
  canDelete: boolean;
  createdAt: string;
  /** One level only. */
  replies: ForumComment[];
}

/** The three activity tabs on the Messages page. */
export type ActivityKind = "likes" | "follows" | "comments";

/** Mirrors `ActivityItemDto`. */
export interface ActivityItem {
  /** Stable id; pass it back to mark the item read. */
  key: string;
  kind: string;
  /** Null for an anonymous commenter. */
  actor: { id: string; name: string; photoUrl: string | null; role: string } | null;
  /** Shown after the name: "started following you". */
  action: string;
  targetTitle: string | null;
  snippet: string | null;
  href: string;
  createdAt: string;
  isRead: boolean;
}

/** Mirrors `ActivityCountsDto`: unread per tab. */
export interface ActivityCounts {
  likes: number;
  follows: number;
  comments: number;
}
