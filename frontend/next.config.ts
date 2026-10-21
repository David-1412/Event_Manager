import type { NextConfig } from "next";

/**
 * Where the web server forwards the API's paths to: `/api/*`, the `/hubs/*`
 * realtime endpoint and `/uploads/*` (host-uploaded event images).
 *
 * The browser normally calls the API at NEXT_PUBLIC_API_BASE_URL. When the site is
 * served over https and that address is plain http (the VPS's `http://<ip>:5000`),
 * the browser blocks every such request as mixed content - API calls, uploads and
 * images alike - so lib/api.ts falls back to the site's own origin, and these
 * rewrites carry the request on to the API from the server side. Uploaded images
 * are always loaded this way (`/uploads/<key>.png` is stored root-relative), so they
 * work on whatever domain the site runs on.
 *
 * Server-to-server, so this is the API's address *inside* the deployment: the
 * compose service `api` on its container port in a Docker build (where `next
 * build` runs with NODE_ENV=production), the API's public URL under `next dev`.
 * API_INTERNAL_URL overrides both. Rewrites are resolved at build time.
 */
const apiOrigin = (
  process.env.API_INTERNAL_URL ??
  (process.env.NODE_ENV === "production"
    ? "http://api:8080"
    : process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5000")
).replace(/\/$/, "");

const nextConfig: NextConfig = {
  // The frontend is its own npm project with its own lockfile, but this repo can
  // also end up with a stray root package-lock.json (any `npm install` run from
  // the repo root creates one). Next then infers the workspace root as the repo
  // root instead of frontend/, which breaks module resolution under `next dev`
  // (Turbopack) with "Module not found" for paths inside next/dist. Pinning the
  // root makes inference deterministic, so the dev server survives whichever
  // lockfiles exist — including after `docker compose build` runs `npm ci`.
  outputFileTracingRoot: __dirname,

  async rewrites() {
    return [
      { source: "/api/:path*", destination: `${apiOrigin}/api/:path*` },
      { source: "/hubs/:path*", destination: `${apiOrigin}/hubs/:path*` },
      { source: "/uploads/:path*", destination: `${apiOrigin}/uploads/:path*` },
    ];
  },
};

export default nextConfig;
