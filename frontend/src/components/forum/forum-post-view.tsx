"use client";

import Link from "next/link";
import { useId, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { useSWRConfig } from "swr";
import { CardError } from "@/components/account/card-error";
import { CommentsSection } from "@/components/forum/comments-section";
import { PostAuthor, PostInterestButton, PostMeta } from "@/components/forum/post-card";
import { MessageButton } from "@/components/messages/message-button";
import { ActionMenu, type ActionMenuItem } from "@/components/ui/action-menu";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { deleteForumPost, isForumPostsKey, updateForumPost } from "@/features/forum/forum-api";
import { useForumPost, useRecordPostView } from "@/features/forum/use-forum";
import { ApiError } from "@/lib/api";
import type { ForumPost } from "@/types/forum";

/** Mirror ForumService.TitleMaxLength / ContentMaxLength. */
const TITLE_MAX = 150;
const CONTENT_MAX = 10_000;

/**
 * `/forum/[id]`: one post in full.
 *
 * The body is rendered as plain text with its line breaks kept
 * (`whitespace-pre-wrap`). React escapes it, so nothing a poster types can become
 * markup - which is the reason posts are plain text rather than HTML or Markdown.
 *
 * The author gets a "..." menu to edit (in place) or delete; an admin gets delete. A
 * post deleted while it had comments still opens here as a placeholder, so the
 * thread under it stays readable.
 */
export function ForumPostView() {
  const params = useParams<{ id: string }>();
  const id = params?.id ?? "";
  const { post, error, isLoading, retry } = useForumPost(id);
  useRecordPostView(id);

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-6">
      <Link href="/forum" className="text-meta text-fg-muted no-underline hover:text-fg">
        Back to forum
      </Link>

      <div className="mt-3">
        {error ? (
          <CardError message="This post doesn't exist or has been removed." onRetry={retry} />
        ) : isLoading || !post ? (
          <Card className="p-5">
            <SkeletonBlock className="h-4 w-32" />
            <SkeletonBlock className="mt-4 h-7 w-3/4" />
            <SkeletonBlock className="mt-4 h-24 w-full" />
          </Card>
        ) : (
          <Card className="p-5 sm:p-6">{post.isDeleted ? <DeletedPost post={post} /> : <LivePost post={post} />}</Card>
        )}
        {post && <CommentsSection post={post} />}
      </div>
    </div>
  );
}

function DeletedPost({ post }: { post: ForumPost }) {
  return (
    <article>
      <PostMeta post={post} />
      <p className="mt-3 text-body italic text-fg-muted">[This post has been deleted]</p>
      <div className="mt-2 text-meta">
        <PostAuthor post={post} />
      </div>
    </article>
  );
}

function LivePost({ post }: { post: ForumPost }) {
  const router = useRouter();
  const { mutate } = useSWRConfig();
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);

  async function remove() {
    const thread = post.commentCount > 0 ? " The comments stay, under a deleted placeholder." : "";
    if (!window.confirm(`Delete this post?${thread}`)) return;
    setDeleting(true);
    try {
      await deleteForumPost(post.id);
      void mutate(isForumPostsKey);
      toast("Post deleted", "success");
      router.push("/forum");
    } catch {
      toast("Couldn't delete the post. Try again.", "info");
      setDeleting(false);
    }
  }

  const actions: ActionMenuItem[] = [
    ...(post.canEdit ? [{ label: "Edit", onSelect: () => setEditing(true) }] : []),
    ...(post.canDelete ? [{ label: "Delete", onSelect: () => void remove(), danger: true }] : []),
  ];

  if (editing) {
    return (
      <PostEditor
        post={post}
        onCancel={() => setEditing(false)}
        onSaved={async () => {
          await mutate(isForumPostsKey);
          setEditing(false);
          toast("Post updated", "success");
        }}
      />
    );
  }

  return (
    <article aria-busy={deleting || undefined}>
      <div className="flex items-start gap-2">
        <PostMeta post={post} className="min-w-0 flex-1" />
        <ActionMenu label="Post actions" items={actions} className="-mr-2 -mt-1" />
      </div>
      {post.title && <h1 className="mt-3 text-h1 text-fg">{post.title}</h1>}
      <div className="mt-3 flex flex-wrap items-center gap-3">
        <PostAuthor post={post} size="md" />
        {/* Named posts only: an anonymous post never reveals who to message,
            and an admin who can see the author should not use it to reach them. */}
        {!post.isAnonymous && (
          <MessageButton
            userId={post.author?.id}
            label="Message author"
            draft={`Hi! About your post "${post.title ?? "in the forum"}": `}
          />
        )}
      </div>
      <p className="mt-4 whitespace-pre-wrap break-words text-body text-fg">{post.content}</p>
      <div className="mt-5 flex items-center gap-3 border-t border-border pt-3 text-meta text-fg-muted">
        <PostInterestButton post={post} />
        <span className="tabular-nums">
          {post.commentCount} {post.commentCount === 1 ? "comment" : "comments"}
        </span>
        <span className="tabular-nums">
          {post.viewCount.toLocaleString("en-AU")} {post.viewCount === 1 ? "view" : "views"}
        </span>
      </div>
    </article>
  );
}

/** The post's title and body as an inline form. Category and anonymity stay as posted. */
function PostEditor({
  post,
  onCancel,
  onSaved,
}: {
  post: ForumPost;
  onCancel: () => void;
  onSaved: () => Promise<void>;
}) {
  const [title, setTitle] = useState(post.title ?? "");
  const [content, setContent] = useState(post.content);
  const [saving, setSaving] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const titleId = useId();
  const bodyId = useId();

  const unchanged = title.trim() === (post.title ?? "") && content.trim() === post.content;

  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (!content.trim() || saving) return;
    if (unchanged) {
      onCancel();
      return;
    }
    setSaving(true);
    setFailure(null);
    try {
      await updateForumPost(post.id, { title: title.trim() || null, content: content.trim() });
      await onSaved();
    } catch (error) {
      setFailure(
        error instanceof ApiError
          ? error.errors.detail?.[0] || error.detail || "Couldn't save your changes."
          : "Couldn't save your changes.",
      );
      setSaving(false);
    }
  }

  return (
    <form onSubmit={(e) => void save(e)} className="flex flex-col gap-3" aria-label="Edit post">
      <PostMeta post={post} />
      <label htmlFor={titleId} className="text-meta font-medium text-fg">
        Title <span className="font-normal text-fg-muted">(optional)</span>
      </label>
      <input
        id={titleId}
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        maxLength={TITLE_MAX}
        className="w-full rounded-md border border-border bg-surface px-3 py-2 text-body text-fg focus:border-brand-600 focus:outline-none"
      />
      <label htmlFor={bodyId} className="text-meta font-medium text-fg">
        Post
      </label>
      <textarea
        id={bodyId}
        value={content}
        onChange={(e) => setContent(e.target.value)}
        maxLength={CONTENT_MAX}
        rows={8}
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
        <Button type="submit" size="sm" disabled={!content.trim()} loading={saving}>
          Save
        </Button>
      </div>
    </form>
  );
}
