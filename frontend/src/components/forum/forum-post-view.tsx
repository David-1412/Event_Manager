"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { CardError } from "@/components/account/card-error";
import { PostAuthor, PostInterestButton, PostMeta } from "@/components/forum/post-card";
import { Card } from "@/components/ui/card";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { useForumPost } from "@/features/forum/use-forum";

/**
 * `/forum/[id]`: one post in full.
 *
 * The body is rendered as plain text with its line breaks kept
 * (`whitespace-pre-wrap`). React escapes it, so nothing a poster types can become
 * markup - which is the reason posts are plain text rather than HTML or Markdown.
 */
export function ForumPostView() {
  const params = useParams<{ id: string }>();
  const id = params?.id ?? "";
  const { post, error, isLoading, retry } = useForumPost(id);

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
          <Card className="p-5 sm:p-6">
            <article>
              <PostMeta post={post} />
              {post.title && <h1 className="mt-3 text-h1 text-fg">{post.title}</h1>}
              <div className="mt-3">
                <PostAuthor post={post} size="md" />
              </div>
              <p className="mt-4 whitespace-pre-wrap break-words text-body text-fg">{post.content}</p>
              <div className="mt-5 flex items-center gap-3 border-t border-border pt-3 text-meta text-fg-muted">
                <PostInterestButton post={post} />
                <span className="tabular-nums">
                  {post.commentCount} {post.commentCount === 1 ? "comment" : "comments"}
                </span>
              </div>
            </article>
          </Card>
        )}
      </div>
    </div>
  );
}
