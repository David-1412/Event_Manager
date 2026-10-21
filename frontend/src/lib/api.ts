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

const CONFIGURED_API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";

/** No API configured at all: the app runs on its built-in fixtures. Decided from
 *  the configured value, not the resolved one below, which can legitimately be "". */
export const usingFixtures = CONFIGURED_API_BASE_URL === "";

/**
 * The origin API calls are prefixed with. Normally the configured
 * NEXT_PUBLIC_API_BASE_URL - but on an https page a plain-http API address would
 * have every call, upload and image blocked as mixed content, so the browser uses
 * the site's own origin instead ("", i.e. `/api/...`), which the web server
 * forwards to the API (next.config.ts). On the server, and on an http page, the
 * configured value stands.
 */
export function resolveApiBaseUrl(configured: string, pageProtocol: string | undefined): string {
  if (pageProtocol === "https:" && /^http:\/\//i.test(configured)) return "";
  return configured;
}

export const API_BASE_URL = resolveApiBaseUrl(
  CONFIGURED_API_BASE_URL,
  typeof window === "undefined" ? undefined : window.location.protocol,
);

/**
 * Resolve an uploaded-media URL to something a browser <img> can load.
 *
 * - Absolute (`http...`): used as-is.
 * - Root-relative (`/uploads/<key>.png`, what the upload endpoint returns and the
 *   event stores): used as-is, against the page's own origin. The web server
 *   forwards `/uploads/*` to the API (next.config.ts), so this loads on any domain
 *   the site is served from - including https, where an `http://<ip>:5000` image
 *   would be blocked as mixed content.
 * - Bare (`uploads/x.png`, from an older or hand-made row): made root-relative.
 *
 * Empty input stays empty so callers can branch on falsy rather than on this
 * function's output.
 */
export function mediaUrl(url: string | null | undefined): string | null {
  const value = url?.trim();
  if (!value) return null;
  if (/^https?:\/\//i.test(value) || value.startsWith("/")) return value;
  return `/${value}`;
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
  /** Override the default 10 s ceiling for a call that is expected to be slow. */
  timeoutMs?: number;
}

/**
 * The transport every non-SWR call goes through: timeout, optional abort,
 * bearer token, and Problem Details mapped to `ApiError`. `swr-fetcher` covers
 * reads; this covers the writes (`POST /api/events`, participants) that need a
 * timeout and a token.
 */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), options.timeoutMs ?? TIMEOUT_MS);
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
