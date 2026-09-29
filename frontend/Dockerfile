# syntax=docker/dockerfile:1
# Build context = repository root (set by docker-compose.yml).

# ---- deps -------------------------------------------------------------------
# node_modules is isolated in its own layer so `next build` gets a full install
# (devDependencies included) without the runtime image carrying it. Node 24
# matches the host (`.nvmrc`-equivalent: the dev machine runs 24.x), so the
# engines the lockfile resolves for are the engines the container uses.
FROM node:24-alpine AS deps
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
# On TLS-intercepting corporate networks (Zscaler and friends) the registry's
# certificate is signed by a CA no base image trusts, and `npm ci` stalls with
# ETIMEDOUT. Pass that CA with `docker compose build --secret
# id=corp-ca,src=<ca-chain.pem>`; it is mounted for this RUN only, so it never
# lands in an image layer. Without the secret the line is a no-op.
RUN --mount=type=secret,id=corp-ca,target=/tmp/corp-ca.pem,required=false \
    if [ -f /tmp/corp-ca.pem ]; then cat /tmp/corp-ca.pem >> /etc/ssl/certs/ca-certificates.crt; fi && \
    NODE_EXTRA_CA_CERTS=/etc/ssl/certs/ca-certificates.crt npm ci

# ---- build ------------------------------------------------------------------
FROM node:24-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY frontend/ ./
# NEXT_PUBLIC_* are inlined into the client bundle at build time, so they arrive
# as build args (docker-compose.yml passes them from the host environment).
# Defaults keep the map/auth silent fallbacks working with none present.
ARG NEXT_PUBLIC_API_BASE_URL=http://localhost:5000
ARG NEXT_PUBLIC_GOOGLE_MAPS_API_KEY=
ARG NEXT_PUBLIC_GOOGLE_MAPS_MAP_ID=
ARG NEXT_PUBLIC_FIREBASE_API_KEY=
ARG NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN=
ARG NEXT_PUBLIC_FIREBASE_PROJECT_ID=
ARG NEXT_PUBLIC_FIREBASE_STORAGE_BUCKET=
ARG NEXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID=
ARG NEXT_PUBLIC_FIREBASE_APP_ID=
ARG NEXT_PUBLIC_FIREBASE_MEASUREMENT_ID=
ENV NEXT_PUBLIC_API_BASE_URL=$NEXT_PUBLIC_API_BASE_URL \
    NEXT_PUBLIC_GOOGLE_MAPS_API_KEY=$NEXT_PUBLIC_GOOGLE_MAPS_API_KEY \
    NEXT_PUBLIC_GOOGLE_MAPS_MAP_ID=$NEXT_PUBLIC_GOOGLE_MAPS_MAP_ID \
    NEXT_PUBLIC_FIREBASE_API_KEY=$NEXT_PUBLIC_FIREBASE_API_KEY \
    NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN=$NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN \
    NEXT_PUBLIC_FIREBASE_PROJECT_ID=$NEXT_PUBLIC_FIREBASE_PROJECT_ID \
    NEXT_PUBLIC_FIREBASE_STORAGE_BUCKET=$NEXT_PUBLIC_FIREBASE_STORAGE_BUCKET \
    NEXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID=$NEXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID \
    NEXT_PUBLIC_FIREBASE_APP_ID=$NEXT_PUBLIC_FIREBASE_APP_ID \
    NEXT_PUBLIC_FIREBASE_MEASUREMENT_ID=$NEXT_PUBLIC_FIREBASE_MEASUREMENT_ID
RUN npm run build

# ---- runtime ----------------------------------------------------------------
FROM node:24-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production PORT=3000 HOSTNAME=0.0.0.0
# `npm start` loads next.config.ts by transpiling it at runtime, which resolves
# `import type { NextConfig } from "next"` against node_modules — so the config
# and tsconfig have to ship with the image. node_modules comes from `deps`,
# which means the .env.local that `COPY frontend/` put into the build stage is
# absent here: localhost URLs and keys stay out of the runtime image, and
# NEXT_PUBLIC_* were already inlined into .next at build time.
COPY --from=deps /app/node_modules ./node_modules
COPY --from=build /app/.next ./.next
COPY --from=build /app/public ./public
COPY --from=build /app/package.json ./package.json
COPY --from=build /app/next.config.ts ./next.config.ts
COPY --from=build /app/tsconfig.json ./tsconfig.json
EXPOSE 3000
# npm start must not run as root, and node_modules is root-owned from `deps`.
# chown into a fresh directory rather than rewriting the tree in place.
RUN adduser -D -h /app appuser && chown appuser /app
USER appuser
CMD ["npm", "start"]
