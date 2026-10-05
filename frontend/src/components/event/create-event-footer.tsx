"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";
import { StickyFooter } from "@/components/layout/site-header";

/**
 * Sticky footer CTA on mobile, inline row at `lg+` (spec §6). One submit
 * button per breakpoint so the sticky bar never duplicates a form owner.
 *
 * `onCancel`, when provided, replaces the plain Cancel link's navigation with a
 * caller-controlled exit (used in draft review mode to save/prompt before leaving).
 * When absent, Cancel is a normal link to `/`.
 */
export function CreateEventFooter({
  submitting,
  submittingLabel,
  onCancel,
  onSave,
  saveBusy,
}: {
  submitting: boolean;
  submittingLabel?: string;
  onCancel?: () => void;
  /** Draft review mode: persist the in-progress payload without publishing. */
  onSave?: () => void;
  saveBusy?: boolean;
}) {
  const label = submittingLabel ?? "Create event";
  return (
    <>
      <div className="hidden items-center gap-2 lg:flex">
        <Button type="submit" size="lg" loading={submitting}>
          {label}
        </Button>
        {onSave && (
          <Button variant="secondary" size="lg" onClick={onSave} loading={saveBusy}>
            Save draft
          </Button>
        )}
        {onCancel ? (
          <Button variant="secondary" size="lg" onClick={onCancel}>
            Cancel
          </Button>
        ) : (
          <Link
            href="/"
            className="press inline-flex h-12 items-center rounded-md border border-border bg-surface px-5 text-body font-medium text-fg no-underline hover:bg-surface-2"
          >
            Cancel
          </Link>
        )}
      </div>
      <div className="lg:hidden">
        <StickyFooter>
          <div className="flex w-full gap-2">
            <Button type="submit" size="lg" fullWidth loading={submitting}>
              {label}
            </Button>
            {onSave && (
              <Button variant="secondary" size="lg" fullWidth onClick={onSave} loading={saveBusy}>
                Save draft
              </Button>
            )}
          </div>
        </StickyFooter>
      </div>
    </>
  );
}

