# syntax=docker/dockerfile:1
# Build context = repository root (set by docker-compose.yml).

# backend/Dockerfile.dockerignore excludes frontend/ and everything else this
# stage never COPYs, which keeps the root build context down to a few kB.

# Corporate networks that TLS-intercept (Zscaler and friends) hand containers a
# certificate signed by a CA no base image trusts, and `dotnet restore` dies
# with NU1301. Pass that CA with `scripts/export-corp-ca.ps1` (docker-compose.yml
# mounts ./corp-ca.pem as the `corp-ca` secret); the mount exists only for the
# duration of the RUN it is attached to, so the CA never lands in an image layer.
# Without the secret the conditional is a no-op.
#
# Even with the right CA this proxy still resets connections to api.nuget.org at
# random, so restore retries with growing sleeps. The primary store is a BuildKit
# cache mount on /root/.nuget/packages: once a package has been fetched through
# any build, later builds read it from there without the network, and because the
# mount is writable NuGet records packages correctly rather than reading a frozen
# snapshot.
#
# The cache mount is the only store NuGet can *write*, so it is also the only one
# that can end up complete. The two read-only inputs alongside it cannot finish a
# build on their own, because NuGet validates that every package an assets file
# references is present under the *current* global-packages folder, and that
# folder has to be where the packages physically are:
#   - the `offline` named context (docker-compose.yml passes a flat NuGet v2
#     folder built from the host cache via `--build-context`; scripts/restore-cache.ps1
#     builds that folder). A useful extra source, but only a partial mirror of
#     this solution's graph: the host resolves against its own installed SDKs and
#     runtimes, so some packages a net8.0 container needs can be absent from it.
#     When that happens the restore below falls back to the registry with the
#     folder still in the source list, so it only has to supply the gap.
#   - project.assets.json copied from the restore stage, which is why restore and
#     publish share one cache-mounted RUN. Split across stages, the copied assets
#     file is evaluated against a global-packages folder that was not the one it
#     was produced with, and publish fails with NETSDK1064 on packages that the
#     restore stage did successfully download.
# So restore and build happen together, against the cache mount, and publish uses
# `--no-restore` only to stop MSBuild re-resolving against the network.
#
# With neither a reachable registry nor a populated cache mount, restore fails with
# NU1301/NU1101 naming the packages, which is the correct outcome rather than a
# mystery.

# ---- build --------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Everything the restore+publish layer needs, copied before any build runs, so a
# change confined to other paths (prober, tests) does not invalidate it.
COPY backend/Directory.Build.props backend/
COPY backend/build.nuget.config backend/
COPY backend/build-fallback.nuget.config backend/
COPY backend/src/SportMeet.Api/ backend/src/SportMeet.Api/
COPY backend/src/SportMeet.Application/ backend/src/SportMeet.Application/
COPY backend/src/SportMeet.Domain/ backend/src/SportMeet.Domain/
COPY backend/src/SportMeet.Infrastructure/ backend/src/SportMeet.Infrastructure/

RUN --mount=type=secret,id=corp-ca,target=/tmp/corp-ca.pem,required=false \
    --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    --mount=from=offline,target=/offline,ro \
    if [ -f /tmp/corp-ca.pem ]; then cp /tmp/corp-ca.pem /usr/local/share/ca-certificates/corp-ca.pem && update-ca-certificates >/dev/null; fi && \
    if [ -n "$(ls -A /offline 2>/dev/null)" ]; then \
      echo "restoring: BuildKit package cache + host NuGet folder as extra source" >&2; \
      if ! dotnet restore --locked-mode --configfile backend/build.nuget.config --source /offline backend/src/SportMeet.Api/SportMeet.Api.csproj; then \
        echo "host NuGet folder did not cover the graph; retrying with the registry added to it" >&2; \
        cp backend/build-fallback.nuget.config backend/src/SportMeet.Api/nuget.config; \
        dotnet restore --locked-mode --configfile backend/src/SportMeet.Api/nuget.config backend/src/SportMeet.Api/SportMeet.Api.csproj; \
        rm -f backend/src/SportMeet.Api/nuget.config; \
      fi; \
    else \
      echo "restoring: BuildKit package cache, then the registry" >&2; \
      for n in 1 2 3 4; do \
        if dotnet restore --locked-mode backend/src/SportMeet.Api/SportMeet.Api.csproj; then break; fi; \
        if [ "$n" = 4 ]; then echo "registry unreachable and no host cache supplied: run scripts/restore-cache.ps1 once to populate the offline NuGet source, or set NUGET_PACKAGES_PATH (scripts/build.ps1 does) / pass --build-context offline=<flat nupkg folder>" >&2; exit 1; fi; \
        echo "restore attempt $n failed; retrying in $((n * 5))s" >&2; sleep $((n * 5)); \
      done; \
    fi && \
    dotnet publish --no-restore backend/src/SportMeet.Api/SportMeet.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# The healthcheck probe (see backend/prober/Program.cs). Built separately and
# copied into the runtime as a framework-dependent app; its only import is
# System.Net.Http, so it runs on the runtime this image already has.
# It has no PackageReference entries, so a restore here resolves locally without
# touching a registry.
COPY backend/prober/ /probe/
RUN dotnet publish /probe/prober.csproj -c Release -o /probe/out /p:UseAppHost=false

# ---- runtime ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# Run as the non-root user the base image ships. The image already carries
# ICU; InvariantGlobalization is false in Directory.Build.props, so don't strip it.
COPY --from=build /app/publish ./
COPY --from=build /probe/out /probe/
# Kestrel listens on plain HTTP: TLS termination belongs to whatever fronts
# this container, and Program.cs redirects to HTTPS outside Development, so the
# compose file keeps the container in Development.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Host-uploaded event thumbnails live under App_Data/uploads (a compose volume).
# Create it here, BEFORE USER, so it is owned by the non-root app user; Docker
# seeds a fresh named volume from this directory's ownership, letting the API
# write into it at runtime. (After USER the base image is already non-root, so a
# mkdir there would land root-owned and unwritable.)
RUN mkdir -p /app/App_Data/uploads && chown -R "$APP_UID" /app/App_Data
USER $APP_UID
ENTRYPOINT ["dotnet", "SportMeet.Api.dll"]
