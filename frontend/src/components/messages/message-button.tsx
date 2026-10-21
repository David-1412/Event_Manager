"use client";

import { usePathname, useRouter } from "next/navigation";
import { useState } from "react";
import { MessageCircle } from "lucide-react";
import { toast } from "@/components/ui/toast";
import { messagesHref, startConversation } from "@/features/messages/messages-api";
import { ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { cn } from "@/lib/cn";

/**
 * "Message" - open the direct-message thread with someone: an event's host, a
 * post's author, a profile's owner.
 *
 * Renders nothing for your own id (there is no one to message) or when there is no
 * id at all - which is the case for an anonymous post, whose author the API never
 * sends. That is the anonymity rule doing the work: you cannot message someone you
 * cannot identify, so the button cannot become a way to reach, and so unmask, an
 * anonymous poster.
 *
 * `draft` pre-fills the composer ("Hi! About Netball Tuesday: "), so the recipient
 * knows what the message is about without a separate context field.
 */
export function MessageButton({
  userId,
  draft,
  className,
  label = "Message",
}: {
  userId: string | null | undefined;
  draft?: string;
  className?: string;
  label?: string;
}) {
  const { user, loading } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [pending, setPending] = useState(false);

  if (!userId || loading || user?.id === userId) return null;

  async function open() {
    if (!user) {
      router.push(signInReturnTo(pathname ?? "/"));
      return;
    }
    setPending(true);
    try {
      const conversation = await startConversation(userId!);
      router.push(messagesHref(conversation.id, draft));
    } catch (error) {
      toast(
        error instanceof ApiError && error.status === 401
          ? "Sign in again to send messages."
          : "Couldn't open the conversation. Try again.",
        "info",
      );
      setPending(false);
    }
  }

  return (
    <button
      type="button"
      onClick={() => void open()}
      disabled={pending}
      className={cn(
        "press inline-flex h-8 shrink-0 items-center gap-1.5 rounded-full border border-border bg-surface px-3 text-xs font-semibold text-fg hover:bg-surface-2 disabled:opacity-60",
        className,
      )}
    >
      <MessageCircle aria-hidden size={14} />
      {label}
    </button>
  );
}
