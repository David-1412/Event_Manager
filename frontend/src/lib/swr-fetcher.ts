import { ApiError, type ProblemDetails } from "@/lib/api";
import { getAccessToken } from "@/lib/auth/token-store";

/**
 * Single fetch entry point for SWR. SWR passes the key (a full URL) straight
 * through, so `eventsKey()`/`detailKey()` must already be absolute whenever the
 * real API is configured.
 *
 * The bearer token is read at call time rather than baked into the cache key:
 * the browse list is identical signed-in and signed-out, and keying every read
 * on the token would throw the cache away on each hourly refresh. Detail
 * endpoints, which do carry `isHost`/`isJoined`, are revalidated by the caller
 * when the session changes.
 */
export async function fetcher<T>(url: string): Promise<T> {
  const token = getAccessToken();
  const response = await fetch(url, {
    headers: {
      Accept: "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });
  const text = await response.text();
  const payload: unknown = text ? tryParse(text) : null;

  if (!response.ok) {
    const problem =
      payload && typeof payload === "object" && "status" in payload
        ? (payload as ProblemDetails)
        : {
            status: response.status,
            title: response.statusText || "Request failed",
          };
    throw new ApiError({ code: "Unknown", ...problem });
  }
  return payload as T;
}

function tryParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

