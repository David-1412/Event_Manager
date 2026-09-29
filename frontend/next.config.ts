import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The frontend is its own npm project with its own lockfile, but this repo can
  // also end up with a stray root package-lock.json (any `npm install` run from
  // the repo root creates one). Next then infers the workspace root as the repo
  // root instead of frontend/, which breaks module resolution under `next dev`
  // (Turbopack) with "Module not found" for paths inside next/dist. Pinning the
  // root makes inference deterministic, so the dev server survives whichever
  // lockfiles exist — including after `docker compose build` runs `npm ci`.
  outputFileTracingRoot: __dirname,
};

export default nextConfig;
