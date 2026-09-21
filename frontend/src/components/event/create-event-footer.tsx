"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";
import { StickyFooter } from "@/components/layout/site-header";

/**
 * Sticky footer CTA on mobile, inline row at `lg+` (spec §6). One submit
 * button per breakpoint so the sticky bar never duplicates a form owner.
 */
export function CreateEventFooter({ submitting }: { submitting: boolean }) {
  return (
    <>
      <div className="hidden items-center gap-2 lg:flex">
        <Button type="submit" size="lg" loading={submitting}>
          Create event
        </Button>
        <Link
          href="/"
          className="press inline-flex h-12 items-center rounded-md border border-border bg-surface px-5 text-body font-medium text-fg no-underline hover:bg-surface-2"
        >
          Cancel
        </Link>
      </div>
      <div className="lg:hidden">
        <StickyFooter>
          <Button type="submit" size="lg" fullWidth loading={submitting}>
            Create event
          </Button>
        </StickyFooter>
      </div>
    </>
  );
}
