# MonaHub

Next.js 16 frontend + .NET 8 / EF Core API + PostgreSQL 16, runnable entirely in Docker.

## Run it in Docker

```powershell
docker compose up -d --build
docker compose ps            # wait until postgres and api report (healthy)
```

Then open:

| URL | What |
| --- | --- |
| <http://localhost:3000> | web — production Next.js build |
| <http://localhost:5000> | api — Swagger UI (Development only) |
| <http://localhost:5000/healthz> | api health probe |

`docker compose up` alone is enough for a normal machine: each Dockerfile restores
through a BuildKit cache that survives between builds, so only the first build
touches the network.

Useful commands:

```powershell
docker compose logs -f api web   # follow logs
docker compose restart api       # restart one service
docker compose down              # stop (keeps the postgres volume + seeded data)
docker compose down -v           # stop AND delete the database volume
```

### Behind a TLS-intercepting proxy (corporate network)

Networks that intercept TLS (Zscaler and similar) hand containers a certificate no
base image trusts, so `dotnet restore` fails with NU1301 and `npm ci` hangs. Export
the proxy's CA from the Windows certificate store first — this is what `scripts/build.ps1`
does automatically:

```powershell
powershell -File scripts/export-corp-ca.ps1          # writes .\corp-ca.pem
powershell -File scripts/build.ps1                   # builds both images
```

`corp-ca.pem` is mounted as a BuildKit *secret*, so it never lands in an image layer.
The `-Issuer` default is `Zscaler`; pass another to retarget.

### If port 3000 or 5000 is already in use

Set the host-side port (the containers keep listening internally on 3000 / 8080, so
no rebuild is needed):

```powershell
$env:WEB_PORT = "3100"; $env:API_PORT = "5050"
docker compose up -d
```

A common collision is `npm run dev` already holding 3000. Find it with
`Get-NetTCPConnection -LocalPort 3000 -State Listen` and either stop that process or
use `WEB_PORT`.

### Browser-reachable keys

`NEXT_PUBLIC_*` values are **inlined into the JS bundle at build time**, not read at
runtime — changing one requires rebuilding the `web` image
(`docker compose build web`). Put them in a `.env` file at the repo root; compose
interpolates it into the build args:

```env
NEXT_PUBLIC_API_BASE_URL=http://localhost:5000
NEXT_PUBLIC_GOOGLE_MAPS_API_KEY=...
NEXT_PUBLIC_GOOGLE_MAPS_MAP_ID=...
NEXT_PUBLIC_FIREBASE_API_KEY=...
```

All of them are optional: the map degrades to a labelled placeholder without a Maps
key, and the API runs with a demo identity (`{"status":"ok","demoIdentity":true}`)
when Firebase config is absent. `NEXT_PUBLIC_API_BASE_URL` must stay a
**browser-reachable** origin — never `http://api:8080`, which only resolves inside the
Docker network.

## CI/CD

`.github/workflows/ci.yml` runs, in order:

| Job | Runs on | What |
| --- | --- | --- |
| `frontend` | push, PR | `npm ci`, `npm run verify` (encoding, type-check, lint, vitest, build), `npm audit` |
| `backend` | push, PR | `dotnet restore --locked-mode`, `dotnet build` (Release) |
| `images` | after both | `docker buildx bake` of `docker-compose.yml`, pushed to GHCR on `main` only |
| `smoke` | `main` only | pulls the **published images**, runs the stack, checks `/healthz`, `/api/events`, and web HTML |
| `deploy` | `main` only | placeholder that **fails on purpose** until you fill it in |

PRs get a full image build but never receive registry credentials, so a fork cannot
push images.

To enable it: push the repo to GitHub, then in **Settings → Packages** give the
workflow access to the `sportmeet-api` / `sportmeet-web` packages once, so repeated
runs can overwrite them. `GITHUB_TOKEN` authenticates to GHCR by itself — no secret
to create. Create a **production** environment (Settings → Environments) for the
deploy gate.

Images are tagged `latest` and the commit SHA, and their names come from `API_IMAGE`
and `WEB_IMAGE`, which `docker-compose.yml` declares with local defaults:

```powershell
# what CI builds and pushes
$env:API_IMAGE = "ghcr.io/<owner>/sportmeet-api"
$env:WEB_IMAGE = "ghcr.io/<owner>/sportmeet-web"
```

Before a PR, run `npm run verify` locally — CI gates on it, and it caught a real
failure the first time it ran.

## Run it without Docker

```powershell
docker compose up -d postgres                       # database only
dotnet run --project backend/src/SportMeet.Api     # API on :5000, seeded on startup
cd frontend; npm run dev                            # web on :3000
```

The API migrates and seeds PostgreSQL on startup — watch for
`Demo seed complete: 8 sports, 9 events in scope.` in `docker compose logs api` — so
the database must be reachable before it starts.

## Roles and notifications

New accounts are **Members**. Their public events stay out of Browse until an
Admin approves them; private events remain immediately shareable. **Moderators**
can publish public events immediately, but cannot access user management or the
review queue. **Admins** can additionally assign roles, approve and reject
submissions, and manage users. The API enforces these permissions independently
of the frontend navigation.

Hosts can edit their events from My events → Hosting. Editing a public event as
a Member sends it back to PendingReview until an Admin approves the changes;
Members cannot switch a public event to private to bypass review. Moderators and
Admins can edit their own events, including past events, without resubmitting
them; they can publish a public event that was awaiting review.

Admins can manage events at `/admin/events`: search/filter across past, ongoing,
and upcoming events, edit event details and status, and remove events. Removal
is a soft delete: the record stays in `sportsmeet.events`, while the active
event feed and ordinary event queries hide it. `GET /api/admin/events` supports
`q`, `timeFrame` (`past`, `current`, `future`), `status`, `page`, and `pageSize`;
`PUT` updates an event and `DELETE` soft-deletes it.

After an Admin approves a submitted event, the host receives an in-app
notification. Event hosts are also notified when someone joins or expresses
interest, and when either activity reaches 5, 10, 25, or 50. Signed-in users can
open the header bell to see recent notifications, follow event links, mark one
notification read, or mark all as read. The API exposes `GET /api/notifications`,
`PATCH /api/notifications/read`, and
`PATCH /api/notifications/{id}/read`. The role constraint and inbox schema are
applied by the normal startup migration.
