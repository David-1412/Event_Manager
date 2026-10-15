"use client";

import { useState } from "react";
import { Button, type ButtonSize, type ButtonVariant } from "@/components/ui/button";
import { ShareDialog } from "@/components/ui/share-dialog";
import { cn } from "@/lib/cn";
import { canNativeShare, nativeShare, sharePayload, type SharePayload } from "@/lib/share";

interface ShareButtonProps {
  /** Shared as the sheet title and as the modal's subject line. */
  title: string;
  /** The event description; trimmed and clamped in `shareText`. */
  description?: string | null;
  size?: ButtonSize;
  variant?: ButtonVariant;
  fullWidth?: boolean;
  className?: string;
}

/**
 * Share this event: the native sheet where the device has one, the modal where
 * it does not.
 *
 * Takes an event's fields rather than an event, so it drops into the detail page,
 * a browse card or a preview panel without change.
 *
 * The native call happens directly in the click handler and never after a state
 * update. `navigator.share` resolves only while the tap's user activation is
 * still live, so routing it through an effect or an await first would make a
 * phone reject with a NotAllowedError - the failure would look like the API being
 * broken when it is only the ordering. The capability is therefore read at click
 * time, and the modal opens only for the browsers that genuinely lack a sheet.
 *
 * The modal stays reachable on a phone that has both APIs: if the sheet is
 * dismissed or fails, it opens with the link already in the field, which is the
 * same outcome as Copy Link without requiring the person to tap again.
 */
export function ShareButton({
  title,
  description,
  size = "md",
  variant = "secondary",
  fullWidth = false,
  className,
}: ShareButtonProps) {
  // Null until the first tap, which is the only moment the answer is knowable:
  // `canNativeShare` consults `navigator`, absent during the server pass, and it
  // cannot change within a session. Holding it lets the dialog be mounted with a
  // real payload rather than a placeholder.
  const [native, setNative] = useState<boolean | null>(null);
  const [payload, setPayload] = useState<SharePayload | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);

  async function onShare() {
    if (typeof window === "undefined") return;
    const next = sharePayload({ title, description }, window.location);
    setPayload(next);
    const supported = canNativeShare();
    setNative(supported);
    if (!supported) {
      setDialogOpen(true);
      return;
    }
    // Called with `next` already in hand, so nothing is awaited between the tap
    // and `navigator.share` and the user activation is still live.
    const shared = await nativeShare(next);
    // A rejected or dismissed sheet is not worth an error toast - the person
    // changed their mind more often than the device failed - but it should not be
    // a dead end either, so the link is offered instead.
    if (!shared) setDialogOpen(true);
  }

  return (
    <>
      <Button
        type="button"
        size={size}
        variant={variant}
        fullWidth={fullWidth}
        onClick={() => void onShare()}
        className={cn(className)}
        aria-haspopup={native === false ? "dialog" : undefined}
      >
        <span aria-hidden>{"\u{1F517}"}</span>
        Share
      </Button>
      {dialogOpen && payload && (
        <ShareDialog
          open
          onClose={() => setDialogOpen(false)}
          payload={payload}
        />
      )}
    </>
  );
}