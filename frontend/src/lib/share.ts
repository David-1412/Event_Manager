/**
 * The share primitives behind ShareButton, kept free of React and of the DOM so
 * the capability rules and the payload shape can be tested directly.
 *
 * Two channels, one payload. `navigator.share` hands the OS a title, a text and
 * a URL and lets the device choose the destination; everywhere else the URL is
 * put on the clipboard and the person pastes it where they want it. Both read
 * the same `SharePayload`, so what a phone sends to WhatsApp and what a desktop
 * copies are the same three fields rather than two things that drifted apart.
 */
export interface SharePayload {
  title: string;
  text: string;
  url: string;
}

/**
 * Whether the native share sheet is available for a tap that is happening now.
 *
 * `navigator.share` alone is not enough: browsers expose it on insecure origins
 * where it rejects, and it only resolves inside a user activation. The first is
 * checked here so the caller can fall back to the modal instead of opening a
 * sheet that immediately fails; the second is the caller's job - see ShareButton.
 */
export function canNativeShare(nav: Navigator | undefined = typeof navigator === "undefined" ? undefined : navigator): boolean {
  if (!nav || typeof nav.share !== "function") return false;
  if (typeof isSecureContext === "boolean" && !isSecureContext) return false;
  return true;
}

/**
 * The absolute URL of the page being shared.
 *
 * `window.location.href` is what the requirement names, and it is what a reader
 * needs: a shared event should open on the event, not on a filtered browse list.
 * An empty search is dropped so a link copied from `/?q=pickleball` does not
 * carry browse state a recipient never asked for.
 */
export function shareUrl(loc: Pick<Location, "origin" | "pathname" | "search">): string {
  const query = loc.search.startsWith("?") ? loc.search.slice(1) : loc.search;
  const kept = query
    .split("&")
    .filter((pair) => pair !== "" && !pair.startsWith("q="))
    .join("&");
  return `${loc.origin}${loc.pathname}${kept ? `?${kept}` : ""}`;
}

/**
 * Trim and clamp the share text.
 *
 * A long description pasted into a WhatsApp message becomes a wall, and several
 * share targets truncate silently. Cutting at a word boundary keeps the first
 * sentence readable instead of mid-syllable.
 */
export function shareText(description: string | null | undefined, limit = 180): string {
  const text = (description ?? "").replace(/\s+/g, " ").trim();
  if (text.length <= limit) return text;
  const cut = text.slice(0, limit + 1);
  const space = cut.lastIndexOf(" ");
  return `${cut.slice(0, space > 0 ? space : limit).trimEnd()}\u2026`;
}

/** Build the payload from an event's own words plus the current location. */
export function sharePayload(
  event: { title: string; description?: string | null },
  loc: Pick<Location, "origin" | "pathname" | "search">,
): SharePayload {
  return {
    title: event.title,
    text: shareText(event.description),
    url: shareUrl(loc),
  };
}

/**
 * Copy to the clipboard, reporting whether it worked.
 *
 * `navigator.clipboard` is missing on insecure origins and in some in-app
 * webviews, and `writeText` itself rejects when the document has lost focus or
 * permission was refused. Every one of those is the same outcome to the caller:
 * the link did not get copied, so say so rather than claiming success.
 */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (!navigator.clipboard?.writeText) return false;
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

/**
 * Hand the payload to the native sheet.
 *
 * Returns false for both "no sheet" and "the person dismissed it" - `AbortError`
 * is a normal outcome of opening a share sheet and must not surface as a
 * failure toast. Anything else is also swallowed here, because the caller's
 * fallback (a modal with the link in a field) is always the better next step.
 */
export async function nativeShare(payload: SharePayload): Promise<boolean> {
  try {
    await navigator.share({
      title: payload.title,
      text: payload.text,
      url: payload.url,
    });
    return true;
  } catch {
    return false;
  }
}