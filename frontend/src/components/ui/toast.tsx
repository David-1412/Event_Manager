"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { cn } from "@/lib/cn";

export interface ToastMessage {
  id: number;
  text: string;
  tone?: "success" | "info";
}

let sequence = 0;
const listeners = new Set<(toast: ToastMessage) => void>();

/** Transient confirmations only — persistent facts live on the card (spec §7). */
export function toast(text: string, tone: ToastMessage["tone"] = "success") {
  const item: ToastMessage = { id: ++sequence, text, tone };
  listeners.forEach((l) => l(item));
}

const MAX_VISIBLE = 2;
const LIFETIME_MS = 2400;

export function Toaster() {
  const [items, setItems] = useState<ToastMessage[]>([]);
  const timers = useRef(new Map<number, ReturnType<typeof setTimeout>>());

  const dismiss = useCallback((id: number) => {
    setItems((prev) => prev.filter((t) => t.id !== id));
    const timer = timers.current.get(id);
    if (timer) clearTimeout(timer);
    timers.current.delete(id);
  }, []);

  const add = useCallback(
    (item: ToastMessage) => {
      setItems((prev) => [...prev, item].slice(-MAX_VISIBLE));
      timers.current.set(
        item.id,
        setTimeout(() => dismiss(item.id), LIFETIME_MS),
      );
    },
    [dismiss],
  );

  // Subscribe on mount, unsubscribe on unmount - a plain external-system
  // subscription, so the effect owns it rather than a render-time ref check.
  useEffect(() => {
    listeners.add(add);
    const active = timers.current;
    return () => {
      listeners.delete(add);
      active.forEach((timer) => clearTimeout(timer));
      active.clear();
    };
  }, [add]);

  if (typeof document === "undefined") return null;

  return createPortal(
    <div
      aria-live="polite"
      aria-atomic="false"
      className={cn(
        "pointer-events-none fixed inset-x-0 z-50 flex flex-col items-center gap-2 px-4",
        "top-4 lg:inset-x-auto lg:right-4 lg:top-auto lg:bottom-4 lg:items-end lg:px-0",
      )}
    >
      {items.map((t) => (
        <div
          key={t.id}
          className={cn(
            "pointer-events-auto max-w-80 rounded-md border border-border bg-surface px-4 py-3 text-meta text-fg shadow-float",
          )}
        >
          {t.text}
        </div>
      ))}
    </div>,
    document.body,
  );
}
