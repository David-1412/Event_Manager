import { API_BASE_URL, ApiError } from "@/lib/api";
import { getAccessToken } from "@/lib/auth/token-store";

/**
 * Upload one event thumbnail and get back the URL to store on the event.
 *
 * Deliberately not `request()` from lib/api: that helper JSON-encodes its body,
 * and an upload has to go out as multipart/form-data. The browser sets the
 * multipart boundary itself, so no Content-Type is set here. The bearer token is
 * attached the same way request() would, so an authenticated host uploads as
 * themselves; with the demo identity it is simply omitted, matching the endpoint
 * being anonymous-tolerant like POST /api/events.
 *
 * Throws ApiError on a non-2xx (the API answers failures with Problem Details),
 * so the caller can surface the server's own reason ("larger than 5 MB", etc.).
 */
export async function uploadThumbnail(file: File, signal?: AbortSignal): Promise<string> {
  const body = new FormData();
  body.append("file", file);

  const token = getAccessToken();
  const response = await fetch(`${API_BASE_URL}/api/events/thumbnail`, {
    method: "POST",
    body,
    signal,
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError({
      status: response.status,
      title: (problem && typeof problem.title === "string" && problem.title) || "Upload failed",
      code: "Unknown",
    });
  }

  const data = (await response.json()) as { url?: string };
  if (!data.url) throw new ApiError({ status: 502, title: "Upload returned no image URL", code: "Unknown" });
  return data.url;
}
