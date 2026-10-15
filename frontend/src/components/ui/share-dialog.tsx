"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/field";
import { toast } from "@/components/ui/toast";
import { copyText, type SharePayload } from "@/lib/share";

/**
 * The fallback share surface for browsers with no native share sheet - desktop
 * mostly, plus mobile browsers that do not implement `navigator.share`.
 *
 * It is deliberately small. The one job is getting the link out of the app and
 * into wherever the person is going to paste it, so the URL is visible (a link
 * someone can read is a link they can trust and fix up by hand) and Copy Link is
 * the primary action. Everything past that - WhatsApp, Messages, Instagram - is
 * the device's business, which is why the native sheet is preferred when it
 * exists and this is not trying to imitate it with a row of brand buttons that
 * would each need their own share endpoint.
 */
export function ShareDialog({
  open,
  onClose,
  payload,
}: {
  open: boolean;
  onClose: () => void;
  payload: SharePayload;
}) {
  const [copied, setCopied] = useState(false);

  async function onCopy() {
    if (await copyText(payload.url)) {
      setCopied(true);
      toast("\u2705 Link copied to clipboard", "success");
      return;
    }
    // Clipboard access is denied outside a secure context, without permission,
    // or when the tab lost focus. The field is right there and selects itself on
    // focus, so name the manual route instead of just reporting a failure.
    toast("Couldn't copy automatically - select the link and copy it");
  }

  return (
    <Dialog open={open} onClose={onClose} labelledBy="share-title">
      <h2 id="share-title" className="text-h3 text-fg">
        Share this event
      </h2>
      <p className="mt-1 truncate text-meta text-fg-muted">{payload.title}</p>

      <label htmlFor="share-url" className="mt-4 block text-micro uppercase text-fg-muted">
        Event link
      </label>
      <div className="mt-1 flex flex-col gap-2 sm:flex-row">
        <Input
          id="share-url"
          data-autofocus
          readOnly
          value={payload.url}
          aria-label="Event link"
          onFocus={(event) => event.currentTarget.select()}
        />
        <Button
          type="button"
          variant="secondary"
          onClick={() => void onCopy()}
          className="sm:w-36"
        >
          {copied ? "Copied" : "Copy Link"}
        </Button>
      </div>

      <p className="mt-3 text-meta text-fg-muted">
        Anyone with this link can open the event and take a spot.
      </p>

      <div className="mt-5 flex justify-end">
        <Button type="button" variant="ghost" onClick={onClose}>
          Done
        </Button>
      </div>
    </Dialog>
  );
}