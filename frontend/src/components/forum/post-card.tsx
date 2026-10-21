"use client";

import Link from "next/link";
import { HostLink } from "@/components/profile/host-link";
import { Avatar } from "@/components/ui/avatar";
import { FireButton } from "@/components/ui/fire-button";
import { useForumPostInterest } from "@/features/forum/use-forum";
import { cn } from "@/lib/cn";
import { formatTimeAgo } from "@/lib/format";
import type { ForumPost } from "@/types/forum";

export const forumPostHref = (id: string) => `/forum/${id}`;

/**
 * One post in the forum feed.
 *
 * The whole card opens the post, through a single link stretched over it (the
 * `after:inset-0` overlay) rather than a wrapping anchor: the author line is a link
 * of its own, and an anchor inside an anchor is invalid HTML that browsers repair
 * unpredictably. The author row sits above the overlay (`relative z-10`) so it stays
 * clickable.
 *
 * Untitled posts are normal - a quick question in General Chat has no headline - so
 * the body becomes the link text instead of an empty heading.
 */
export function PostCard({ post }: { post: ForumPost }) {
  const href = forumPostHref(post.id);
  const overlay = "after:absolute after:inset-0 after:content-['']";

  return (
    <article className="press relative rounded-md border border-border bg-surface p-4 transition-colors hover:bg-surface-2">
      <PostMeta post={post} />

      {post.title ? (
        <>
          <h3 className="mt-2 text-h3 text-fg">
            <Link href={href} className={cn("no-underline", overlay)}>
              {post.title}
            </Link>
          </h3>
          <p className="mt-1 line-clamp-3 whitespace-pre-line break-words text-body text-fg-muted">
            {post.content}
            {post.isTruncated && "…"}
          </p>
        </>
      ) : (
        <p className="mt-2 line-clamp-4 whitespace-pre-line break-words text-body text-fg">
          <Link href={href} className={cn("text-fg no-underline", overlay)}>
            {post.content}
            {post.isTruncated && "…"}
          </Link>
        </p>
      )}

      <div className="relative z-10 mt-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-meta text-fg-muted">
        <PostAuthor post={post} />
        <span className="ml-auto flex items-center gap-3">
          <span className="tabular-nums">{countLabel(post.commentCount, "comment")}</span>
          <PostInterestButton post={post} />
        </span>
      </div>
    </article>
  );
}

/** Category chip and age - shared by the card and the post page. */
export function PostMeta({ post, className }: { post: ForumPost; className?: string }) {
  return (
    <div className={cn("flex flex-wrap items-center gap-2 text-meta text-fg-muted", className)}>
      <span className="rounded-full bg-brand-tint px-2 py-0.5 text-micro font-medium text-brand-600">
        {post.category.name}
      </span>
      {post.isPinned && <span className="text-micro font-medium uppercase">Pinned</span>}
      <time dateTime={post.createdAt} title={new Date(post.createdAt).toLocaleString("en-AU")}>
        {formatTimeAgo(post.createdAt)}
      </time>
      <EditedMark editedAt={post.editedAt} />
    </div>
  );
}

/** "(edited)" after a timestamp, with when as its tooltip. Nothing when never edited. */
export function EditedMark({ editedAt }: { editedAt?: string | null }) {
  if (!editedAt) return null;
  const when = new Date(editedAt).toLocaleString("en-AU");
  return (
    <span className="italic" title={`Edited ${when}`}>
      (edited)
    </span>
  );
}

/**
 * Who posted. The server has already decided what this viewer may know: `author`
 * is null on an anonymous post for everyone but its writer and admins, so this
 * component cannot leak a name it was never sent.
 */
export function PostAuthor({ post, size = "sm" }: { post: ForumPost; size?: "sm" | "md" }) {
  if (post.isDeleted) {
    return <span className="italic text-fg-muted">[deleted]</span>;
  }
  if (!post.author) {
    return (
      <span className="inline-flex items-center gap-2">
        <Avatar name="Anonymous" size={size} />
        <span>Anonymous</span>
      </span>
    );
  }

  return (
    <span className="inline-flex min-w-0 items-center gap-2">
      <HostLink
        userId={post.author.id}
        name={post.author.name}
        avatarUrl={post.author.photoUrl}
        role={post.author.role}
        size={size}
      />
      {post.isAnonymous && (
        // Only the writer and admins get here. Say so, so neither mistakes an
        // anonymous post for a public one.
        <span className="rounded-full border border-border px-2 py-0.5 text-micro">
          {post.isMine ? "Posted anonymously" : "Anonymous to others"}
        </span>
      )}
    </span>
  );
}

/** "Interested" on a post, with its count - the same red flame control events use. */
export function PostInterestButton({ post }: { post: ForumPost }) {
  const { isInterested, likeCount, pending, toggle } = useForumPostInterest(post);
  return (
    <FireButton
      label="Interested"
      active={isInterested}
      count={likeCount}
      loading={pending}
      onClick={() => void toggle()}
    />
  );
}

function countLabel(count: number, noun: string): string {
  return `${count.toLocaleString("en-AU")} ${noun}${count === 1 ? "" : "s"}`;
}
