"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useSWRConfig } from "swr";
import { forumPostHref } from "@/components/forum/post-card";
import { EmptyState } from "@/components/state/empty-error";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Field, Input, Select, Textarea } from "@/components/ui/field";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { createForumPost, isForumPostsKey } from "@/features/forum/forum-api";
import { useForumCategories } from "@/features/forum/use-forum";
import { ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";

// Mirror ForumService's limits so the counter warns before the server refuses.
const TITLE_MAX = 150;
const CONTENT_MAX = 10_000;

/** The unsent post survives a closed tab - per browser, best effort only. */
const DRAFT_KEY = "monahub.forum.draft";

interface Draft {
  category: string;
  title: string;
  content: string;
  isAnonymous: boolean;
}

const EMPTY: Draft = { category: "", title: "", content: "", isAnonymous: false };

function readDraft(): Draft {
  try {
    const raw = window.localStorage.getItem(DRAFT_KEY);
    return raw ? { ...EMPTY, ...(JSON.parse(raw) as Partial<Draft>) } : EMPTY;
  } catch {
    return EMPTY;
  }
}

function writeDraft(draft: Draft | null) {
  try {
    if (draft) window.localStorage.setItem(DRAFT_KEY, JSON.stringify(draft));
    else window.localStorage.removeItem(DRAFT_KEY);
  } catch {
    // Storage blocked (private window): the form still works, it just won't remember.
  }
}

/**
 * `/forum/new`: write a post.
 *
 * Anonymous posting hides the author from other students, but the post is still
 * tied to the account - the copy under the checkbox says so, because a student who
 * believes an anonymous post is untraceable would be misled about the one thing
 * that matters to them.
 */
export function NewPostView() {
  const router = useRouter();
  const { mutate } = useSWRConfig();
  const { user, loading } = useAuth();
  const { categories } = useForumCategories();
  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [restored, setRestored] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  // Restored after mount, not during render: the server render has no storage, and
  // reading it in the initial state would make the first client render disagree.
  useEffect(() => {
    queueMicrotask(() => {
      setDraft(readDraft());
      setRestored(true);
    });
  }, []);

  useEffect(() => {
    if (restored) writeDraft(draft);
  }, [draft, restored]);

  function update(partial: Partial<Draft>) {
    setDraft((current) => ({ ...current, ...partial }));
  }

  if (loading) {
    return (
      <Shell>
        <SkeletonBlock className="h-64 w-full" />
      </Shell>
    );
  }

  if (!user) {
    return (
      <Shell>
        <EmptyState
          title="Sign in to post in the forum."
          action={
            <Link href={signInReturnTo("/forum/new")} className="no-underline">
              <Button>Sign in</Button>
            </Link>
          }
        />
      </Shell>
    );
  }

  const content = draft.content.trim();
  const canSubmit = Boolean(draft.category) && content.length > 0 && !submitting;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!canSubmit) return;
    setSubmitting(true);
    setFailure(null);
    try {
      const post = await createForumPost({
        category: draft.category,
        title: draft.title.trim() || null,
        content,
        isAnonymous: draft.isAnonymous,
      });
      writeDraft(null);
      // Every cached feed is now missing this post.
      await mutate(isForumPostsKey, undefined, { revalidate: true });
      toast("Posted");
      router.push(forumPostHref(post.id));
    } catch (error) {
      setFailure(describe(error));
      setSubmitting(false);
    }
  }

  return (
    <Shell>
      <Card className="p-5 sm:p-6">
        <form onSubmit={(e) => void submit(e)} className="flex flex-col gap-4">
          <Field label="Category" required>
            {({ id }) => (
              <Select
                id={id}
                value={draft.category}
                onChange={(e) => update({ category: e.target.value })}
                required
              >
                <option value="" disabled>
                  Choose a category
                </option>
                {categories?.map((c) => (
                  <option key={c.slug} value={c.slug}>
                    {c.name}
                  </option>
                ))}
              </Select>
            )}
          </Field>

          <Field label="Title" optional hint={`${draft.title.length}/${TITLE_MAX}`}>
            {({ id, describedBy }) => (
              <Input
                id={id}
                aria-describedby={describedBy}
                value={draft.title}
                maxLength={TITLE_MAX}
                placeholder="e.g. Where's the quietest study spot on campus?"
                onChange={(e) => update({ title: e.target.value })}
              />
            )}
          </Field>

          <Field
            label="Post"
            required
            hint={`${draft.content.length.toLocaleString("en-AU")}/${CONTENT_MAX.toLocaleString("en-AU")}`}
          >
            {({ id, describedBy }) => (
              <Textarea
                id={id}
                aria-describedby={describedBy}
                value={draft.content}
                maxLength={CONTENT_MAX}
                rows={8}
                required
                placeholder="What's on your mind?"
                onChange={(e) => update({ content: e.target.value })}
              />
            )}
          </Field>

          <label className="flex items-start gap-3 rounded-md border border-border p-3">
            <input
              type="checkbox"
              checked={draft.isAnonymous}
              onChange={(e) => update({ isAnonymous: e.target.checked })}
              className="mt-1 h-4 w-4 accent-[var(--color-brand-600)]"
            />
            <span className="flex flex-col gap-0.5">
              <span className="text-body font-medium text-fg">Post anonymously</span>
              <span className="text-meta text-fg-muted">
                Other students won&apos;t see your name. The post is still linked to your
                account, and MonaHub admins can see who wrote it if it&apos;s reported.
              </span>
            </span>
          </label>

          {failure && (
            <p role="alert" className="text-meta text-danger">
              {failure}
            </p>
          )}

          <div className="flex items-center gap-2">
            <Button type="submit" disabled={!canSubmit} loading={submitting}>
              Post
            </Button>
            <Link href="/forum" className="no-underline">
              <Button type="button" variant="ghost">
                Cancel
              </Button>
            </Link>
          </div>
        </form>
      </Card>
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="mx-auto w-full max-w-2xl px-4 py-6">
      <h1 className="mb-4 text-h1 text-fg">New post</h1>
      {children}
    </div>
  );
}

/** The server's own sentence when it has one - a 422 carries it in `errors.detail`. */
function describe(error: unknown): string {
  if (error instanceof ApiError) {
    return error.errors.detail?.[0] || error.detail || error.message || "Could not post. Try again.";
  }
  return "Could not post. Try again.";
}
