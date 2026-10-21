"use client";

import Link from "next/link";
import { useId, useState } from "react";
import { useSWRConfig } from "swr";
import { CornerDownRight } from "lucide-react";
import { EditedMark } from "@/components/forum/post-card";
import { HostLink } from "@/components/profile/host-link";
import { ActionMenu, type ActionMenuItem } from "@/components/ui/action-menu";
import { Avatar } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { isForumPostsKey } from "@/features/forum/forum-api";
import { addComment, deleteComment, updateComment } from "@/features/messages/messages-api";
import { useComments } from "@/features/messages/use-messages";
import { ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { formatTimeAgo } from "@/lib/format";
import type { ForumPost } from "@/types/forum";
import type { ForumComment } from "@/types/messages";

/** Mirrors ForumCommentService.ContentMaxLength. */
const COMMENT_MAX = 5000;

/**
 * Comments on a forum post, with one level of replies.
 *
 * Anonymity follows the post: someone who posted anonymously gets the anonymous
 * box pre-ticked when they reply in their own thread, because replying under their
 * name there would be the easiest way to unmask themselves by accident.
 */
export function CommentsSection({ post }: { post: ForumPost }) {
  const { user, loading } = useAuth();
  const { mutate } = useSWRConfig();
  const { comments, error, isLoading, refresh } = useComments(post.id);
  const [replyingTo, setReplyingTo] = useState<string | null>(null);

  const total = comments?.reduce((n, c) => n + (c.isDeleted ? 0 : 1) + c.replies.length, 0);
  const defaultAnonymous = post.isMine && post.isAnonymous;
  // A deleted post keeps its thread readable, but nothing new can be added to it.
  const canReply = Boolean(user) && !post.isDeleted;

  async function afterChange() {
    await refresh();
    void mutate(isForumPostsKey); // the post's comment count and hot score moved
  }

  return (
    <section aria-labelledby="comments-heading" className="mt-6">
      <h2 id="comments-heading" className="text-h3 text-fg">
        Comments{total !== undefined && <span className="ml-2 text-meta text-fg-muted tabular-nums">{total}</span>}
      </h2>

      <div className="mt-3">
        {post.isDeleted ? (
          <p className="rounded-md border border-dashed border-border px-4 py-3 text-meta text-fg-muted">
            This post was deleted, so it is closed to new comments.
          </p>
        ) : loading ? null : user ? (
          <CommentComposer postId={post.id} defaultAnonymous={defaultAnonymous} onPosted={afterChange} />
        ) : (
          <p className="rounded-md border border-dashed border-border px-4 py-3 text-meta text-fg-muted">
            <Link href={signInReturnTo(`/forum/${post.id}`)} className="font-medium text-brand-600">
              Sign in
            </Link>{" "}
            to join the conversation.
          </p>
        )}
      </div>

      <div className="mt-4">
        {error ? (
          <p className="text-meta text-fg-muted">
            Couldn&apos;t load comments.{" "}
            <button type="button" onClick={() => void refresh()} className="font-medium text-brand-600">
              Try again
            </button>
          </p>
        ) : isLoading || !comments ? (
          <div className="flex flex-col gap-3">
            <SkeletonBlock className="h-16 w-full" />
            <SkeletonBlock className="h-16 w-full" />
          </div>
        ) : comments.length === 0 ? (
          <p className="text-meta text-fg-muted">No comments yet. Be the first.</p>
        ) : (
          <ol className="flex flex-col gap-4">
            {comments.map((comment) => (
              <li key={comment.id}>
                <CommentItem
                  comment={comment}
                  canReply={canReply}
                  onReply={() => setReplyingTo(replyingTo === comment.id ? null : comment.id)}
                  onChanged={afterChange}
                />
                {(comment.replies.length > 0 || replyingTo === comment.id) && (
                  <div className="ml-4 mt-3 flex flex-col gap-3 border-l-2 border-border pl-4 sm:ml-10">
                    {comment.replies.map((reply) => (
                      <CommentItem
                        key={reply.id}
                        comment={reply}
                        canReply={canReply}
                        // A reply to a reply joins this thread (the server flattens it).
                        onReply={() => setReplyingTo(comment.id)}
                        onChanged={afterChange}
                      />
                    ))}
                    {replyingTo === comment.id && (
                      <CommentComposer
                        postId={post.id}
                        parentId={comment.id}
                        defaultAnonymous={defaultAnonymous}
                        compact
                        onCancel={() => setReplyingTo(null)}
                        onPosted={async () => {
                          setReplyingTo(null);
                          await afterChange();
                        }}
                      />
                    )}
                  </div>
                )}
              </li>
            ))}
          </ol>
        )}
      </div>
    </section>
  );
}

/**
 * One comment or reply. A deleted one that still has replies is kept by the server
 * as a placeholder - author "[deleted]", no text - so the replies under it keep
 * their context.
 */
function CommentItem({
  comment,
  canReply,
  onReply,
  onChanged,
}: {
  comment: ForumComment;
  canReply: boolean;
  onReply: () => void;
  onChanged: () => Promise<void>;
}) {
  const [deleting, setDeleting] = useState(false);
  const [editing, setEditing] = useState(false);

  if (comment.isDeleted) {
    return (
      <article aria-label="Deleted comment">
        <p className="text-meta italic text-fg-muted">[deleted]</p>
        <p className="mt-1 text-body italic text-fg-muted">[This comment has been deleted]</p>
      </article>
    );
  }

  async function remove() {
    if (!window.confirm("Delete this comment?")) return;
    setDeleting(true);
    try {
      await deleteComment(comment.id);
      await onChanged();
    } catch {
      toast("Couldn't delete the comment. Try again.", "info");
      setDeleting(false);
    }
  }

  const actions: ActionMenuItem[] = [
    ...(comment.canEdit ? [{ label: "Edit", onSelect: () => setEditing(true) }] : []),
    ...(comment.canDelete ? [{ label: "Delete", onSelect: () => void remove(), danger: true }] : []),
  ];

  return (
    <article className="flex gap-3" aria-busy={deleting || undefined}>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2 text-meta text-fg-muted">
          {comment.author ? (
            <HostLink
              userId={comment.author.id}
              name={comment.author.name}
              avatarUrl={comment.author.photoUrl}
              role={comment.author.role}
            />
          ) : (
            <span className="inline-flex items-center gap-2">
              <Avatar name="Anonymous" size="sm" />
              Anonymous
            </span>
          )}
          {comment.isOp && (
            <span className="rounded-full bg-brand-tint px-1.5 text-micro font-semibold text-brand-600">OP</span>
          )}
          {comment.isAnonymous && comment.author && (
            <span className="rounded-full border border-border px-1.5 text-micro">
              {comment.isMine ? "Posted anonymously" : "Anonymous to others"}
            </span>
          )}
          <time dateTime={comment.createdAt}>{formatTimeAgo(comment.createdAt)}</time>
          <EditedMark editedAt={comment.editedAt} />
          <ActionMenu label="Comment actions" items={actions} className="-my-1 ml-auto" />
        </div>
        {editing ? (
          <CommentEditor
            comment={comment}
            onCancel={() => setEditing(false)}
            onSaved={async () => {
              await onChanged();
              setEditing(false);
            }}
          />
        ) : (
          <>
            <p className="mt-1 whitespace-pre-wrap break-words text-body text-fg">{comment.content}</p>
            {canReply && (
              <div className="mt-1 flex items-center gap-3 text-meta">
                <button
                  type="button"
                  onClick={onReply}
                  className="inline-flex items-center gap-1 font-medium text-fg-muted hover:text-fg"
                >
                  <CornerDownRight aria-hidden size={14} /> Reply
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </article>
  );
}

/** A comment's text as an inline form; Enter-to-submit is left to the button. */
function CommentEditor({
  comment,
  onCancel,
  onSaved,
}: {
  comment: ForumComment;
  onCancel: () => void;
  onSaved: () => Promise<void>;
}) {
  const [text, setText] = useState(comment.content);
  const [saving, setSaving] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const id = useId();

  async function save(e: React.FormEvent) {
    e.preventDefault();
    const content = text.trim();
    if (!content || saving) return;
    if (content === comment.content) {
      onCancel();
      return;
    }
    setSaving(true);
    setFailure(null);
    try {
      await updateComment(comment.id, content);
      await onSaved();
    } catch (error) {
      setFailure(
        error instanceof ApiError
          ? error.errors.detail?.[0] || error.detail || "Couldn't save your edit."
          : "Couldn't save your edit.",
      );
      setSaving(false);
    }
  }

  return (
    <form onSubmit={(e) => void save(e)} className="mt-1 flex flex-col gap-2">
      <label htmlFor={id} className="sr-only">
        Edit comment
      </label>
      <textarea
        id={id}
        value={text}
        onChange={(e) => setText(e.target.value)}
        rows={3}
        maxLength={COMMENT_MAX}
        autoFocus
        className="w-full resize-y rounded-md border border-border bg-surface px-3 py-2 text-body text-fg focus:border-brand-600 focus:outline-none"
      />
      {failure && (
        <p role="alert" className="text-meta text-danger">
          {failure}
        </p>
      )}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" size="sm" onClick={onCancel} disabled={saving}>
          Cancel
        </Button>
        <Button type="submit" size="sm" disabled={!text.trim()} loading={saving}>
          Save
        </Button>
      </div>
    </form>
  );
}

function CommentComposer({
  postId,
  parentId = null,
  defaultAnonymous,
  compact = false,
  onPosted,
  onCancel,
}: {
  postId: string;
  parentId?: string | null;
  defaultAnonymous: boolean;
  compact?: boolean;
  onPosted: () => Promise<void>;
  onCancel?: () => void;
}) {
  const [text, setText] = useState("");
  const [anonymous, setAnonymous] = useState(defaultAnonymous);
  const [posting, setPosting] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const id = useId();

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    const content = text.trim();
    if (!content || posting) return;
    setPosting(true);
    setFailure(null);
    try {
      await addComment(postId, { content, parentCommentId: parentId, isAnonymous: anonymous });
      setText("");
      await onPosted();
    } catch (error) {
      setFailure(
        error instanceof ApiError
          ? error.status === 429
            ? "You're commenting too quickly. Wait a moment."
            : error.errors.detail?.[0] || error.detail || "Couldn't post your comment."
          : "Couldn't post your comment.",
      );
    } finally {
      setPosting(false);
    }
  }

  return (
    <form onSubmit={(e) => void submit(e)} className="flex flex-col gap-2">
      <label htmlFor={id} className="sr-only">
        {parentId ? "Write a reply" : "Write a comment"}
      </label>
      <textarea
        id={id}
        value={text}
        onChange={(e) => setText(e.target.value)}
        rows={compact ? 2 : 3}
        maxLength={COMMENT_MAX}
        placeholder={parentId ? "Write a reply" : "Add a comment"}
        autoFocus={compact}
        className="w-full resize-y rounded-md border border-border bg-surface px-3 py-2 text-body text-fg placeholder:text-fg-muted focus:border-brand-600 focus:outline-none"
      />
      {failure && (
        <p role="alert" className="text-meta text-danger">
          {failure}
        </p>
      )}
      <div className="flex flex-wrap items-center gap-3">
        <label className="inline-flex items-center gap-2 text-meta text-fg-muted">
          <input
            type="checkbox"
            checked={anonymous}
            onChange={(e) => setAnonymous(e.target.checked)}
            className="h-4 w-4 accent-[var(--color-brand-600)]"
          />
          Anonymous
        </label>
        <div className="ml-auto flex items-center gap-2">
          {onCancel && (
            <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
              Cancel
            </Button>
          )}
          <Button type="submit" size="sm" disabled={!text.trim()} loading={posting}>
            {parentId ? "Reply" : "Comment"}
          </Button>
        </div>
      </div>
    </form>
  );
}
