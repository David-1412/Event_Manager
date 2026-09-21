import { ApiError, type ProblemDetails } from "@/lib/api";

/**
 * Single fetch entry point for SWR. SWR passes the key (a full URL) straight
 * through, so `eventsKey()`/`detailKey()` must already be absolute whenever the
 * real API is configured.
 */
export async function fetcher<T>(url: string): Promise<T> {
  const response = await fetch(url, { headers: { Accept: "application/json" } });
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

