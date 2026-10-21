"use client";

import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { ApiError, mediaUrl, usingFixtures } from "@/lib/api";
import { uploadThumbnail } from "@/features/create/thumbnail-api";

/** Client-side ceiling mirroring the endpoint's, so an oversized file is
 * rejected before it ever leaves the browser (spec §7: reject before upload). */
const MAX_BYTES = 5 * 1024 * 1024;
const ACCEPT = "image/jpeg,image/png,image/webp";

/**
 * `/create` "What" thumbnail control. Optional: no image is a valid event.
 *
 * Pick a file -> it uploads immediately to POST /api/events/thumbnail -> the
 * returned URL is handed back through onChange so it flows into the live preview
 * and the create payload. The preview is the uploaded image itself (the same URL
 * the browse card will show), not a local blob, so what you see is what persists.
 * A failure surfaces the server's own reason inline and leaves the value unset.
 */
export function ThumbnailInput({
  id,
  value,
  onChange,
}: {
  id: string;
  value: string | null | undefined;
  onChange: (url: string | null) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const src = mediaUrl(value);
  // The src that failed to load, if any. Keyed by src rather than a boolean so a
  // new upload gets a fresh attempt without an effect to reset it.
  const [brokenSrc, setBrokenSrc] = useState<string | null>(null);

  async function onPick(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    // Reset the input so re-picking the same file after a remove fires change.
    event.target.value = "";
    if (!file) return;

    if (!ACCEPT.split(",").includes(file.type)) {
      setError("Use a JPG, PNG or WebP image");
      return;
    }
    if (file.size > MAX_BYTES) {
      setError("That image is larger than 5 MB");
      return;
    }

    setError(null);
    if (usingFixtures) {
      // No API to upload to while the fixture adapter is active; the URL would
      // never resolve, so say so rather than pretend the upload succeeded.
      setError("Image upload needs the API running");
      return;
    }

    setBusy(true);
    try {
      onChange(await uploadThumbnail(file));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Upload failed - try again");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-2">
      {src ? (
        <div className="relative overflow-hidden rounded-md border border-border">
          {/* Plain <img>, not next/image: the bytes are the host's own upload, served
              same-origin from /uploads (proxied to the API by next.config.ts).
              alt="" - the file name and the Remove control carry the meaning; the
              image is a preview of the host's own upload, not event content. */}
          {brokenSrc === src ? (
            <p className="flex h-40 items-center justify-center bg-surface-2 px-4 text-center text-meta text-fg-muted">
              Uploaded, but the preview couldn&apos;t load. The photo is still attached.
            </p>
          ) : (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={src} alt="" className="h-40 w-full object-cover" onError={() => setBrokenSrc(src)} />
          )}
          <Button
            size="sm"
            variant="secondary"
            className="absolute right-2 top-2"
            onClick={() => onChange(null)}
          >
            Remove
          </Button>
        </div>
      ) : (
        <button
          type="button"
          onClick={() => inputRef.current?.click()}
          disabled={busy}
          className="flex h-28 w-full items-center justify-center rounded-md border border-dashed border-border bg-surface-2 text-meta text-fg-muted hover:bg-surface-2/70 disabled:opacity-60"
        >
          {busy ? "Uploading…" : "Add a photo"}
        </button>
      )}

      <input
        ref={inputRef}
        id={id}
        name="thumbnailUrl"
        type="file"
        accept={ACCEPT}
        className="hidden"
        onChange={onPick}
      />

      {error && <p className="text-meta text-danger">{error}</p>}
    </div>
  );
}
