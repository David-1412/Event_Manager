export type { ProblemDetails } from "@/types/events";
import type { ProblemDetails, JoinFailure } from "@/types/events";
import { getAccessToken } from "@/lib/auth/token-store";

/**
 * Transport layer. `NEXT_PUBLIC_API_BASE_URL` unset => the fixture adapter in
 * `src/lib/fixtures.ts`, which is what the style guide, Vitest and Playwright
 * run against until the .NET API is up. Every function throws `ApiError`.
 */

export class ApiError extends Error {
  readonly status: number;
  readonly code: ProblemDetails["code"];
  readonly errors: Record<string, string[]>;
  readonly detail?: string;

  constructor(problem: ProblemDetails) {
    super(problem.detail || problem.title);
    this.name = "ApiError";
    this.status = problem.status;
    this.code = problem.code;
    this.errors = problem.errors ?? {};
    this.detail = problem.detail;
  }

  static isApiError(value: unknown): value is ApiError {
    return value instanceof ApiError;
  }

  /** JoinButton branches on exactly these three (spec §7). */
  get joinFailure(): JoinFailure {
    if (this.status === 409) return "full";
    if (this.status >= 500 || this.status === 0) return "network";
    return "server";
  }
}

export const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";
export const usingFixtures = API_BASE_URL === "";

/**
 * Resolve an uploaded-media URL (a stored `/uploads/<key>` path) to something a
 * browser <img> can load. The API serves uploads from its own origin, which is
 * NOT the web app's origin, so a relative `/uploads/...` would 404 against the
 * Next server. An already-absolute URL is returned untouched. Empty input stays
 * empty so callers can branch on falsy rather than on this function's output.
 */
export function mediaUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  if (/^https?:\/\//i.test(url)) return url;
  return `${API_BASE_URL}${url.startsWith("/") ? "" : "/"}${url}`;
}

const TIMEOUT_MS = 10_000;

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  signal?: AbortSignal;
  /**
   * Explicit auth header for the session. Leave it out and the current session
   * token from `lib/auth/token-store` is attached automatically; pass
   * `token: null` to force an anonymous call. Ignored while fixtures are active.
   */
  token?: string | null;
}

/**
 * The transport every non-SWR call goes through: timeout, optional abort,
 * bearer token, and Problem Details mapped to `ApiError`. `swr-fetcher` covers
 * reads; this covers the writes (`POST /api/events`, participants) that need a
 * timeout and a token.
 */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), TIMEOUT_MS);
  const signal = options.signal
    ? AbortSignal.any([options.signal, controller.signal])
    : controller.signal;

  const token = options.token === undefined ? getAccessToken() : options.token;

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      method: options.method ?? "GET",
      signal,
      headers: {
        Accept: "application/json",
        ...(options.body !== undefined ? { "Content-Type": "application/json" } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    throw new ApiError({ status: 0, title: "Network unreachable", code: "Unknown" });
  } finally {
    clearTimeout(timeout);
  }

  if (response.status === 204) return undefined as T;

  const text = await response.text();
  const payload: unknown = text ? safeJson(text) : null;

  if (!response.ok) {
    throw new ApiError(
      isProblem(payload)
        ? payload
        : { status: response.status, title: response.statusText || "Request failed", code: "Unknown" },
    );
  }
  return payload as T;
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function isProblem(value: unknown): value is ProblemDetails {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as ProblemDetails).status === "number" &&
    typeof (value as ProblemDetails).title === "string"
  );
}
