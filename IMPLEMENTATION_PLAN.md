# SportMeet — Implementation Plan

A meetup app for local sport games. A stranger creates a badminton game at Glen Waverley,
another stranger discovers it on a map, clicks **Join**, and shows up. Everything else is an enhancement.

- **Repo layout:** monorepo at `Desktop/David` (`backend/`, `frontend/`, `infra/`, `.github/`)
- **Stack:** ASP.NET Core 10 Web API · PostgreSQL (Azure Database for PostgreSQL Flexible Server) · EF Core + Npgsql · Next.js 15 (App Router, TS, Tailwind) · Firebase Auth (JWT) · Google Maps Platform · Azure App Service (Linux) · Vercel · GitHub Actions
- **Non-goals for MVP:** chat, ratings, friends, recurring events, waitlist, push notifications, AI suggestions, mobile app, SignalR.

---

## 0. Toolchain / Prerequisites

| Tool | Status | Action |
|---|---|---|
| .NET SDK | installed (10.0.400) | none |
| git | installed | create GitHub repo `sportmeet` |
| Node.js LTS (22.x) | missing | install (you) |
| Docker Desktop + WSL2 | missing | install (you) — for local Postgres |
| Google Cloud project | not created | enable **Maps JavaScript API**, **Places API**, **Places API (New)**, **Geocoding API** |
| Firebase project | not created | Web app + Email/Password + Google providers; collect Web API key & Project ID |
| Azure subscription | not created | Resource group `sportmeet-rg`, App Service plan `B1` Linux, Flexible Server Postgres |
| Vercel account | not created | import `frontend/` later (Week 4) |

Secrets policy: nothing secret in git. Local dev uses `appsettings.Development.json` (only a connection string to
localhost Postgres) and frontend `.env.local`. Real values live in Azure App Service env vars, GitHub Actions
secrets, and Vercel env vars.

---

## 1. Repository Layout

```
David/
  IMPLEMENTATION_PLAN.md
  .editorconfig
  .gitignore
  Directory.Build.props            # net10.0, Nullable enable, ImplicitUsings, TreatWarningsAsErrors
  SportMeet.sln
  .github/workflows/ci.yml         # build + test + lint on PR
  .github/workflows/deploy-api.yml # push to main -> Azure App Service
  infra/
    docker-compose.yml             # postgres:16-alpine (+ optional pgAdmin)
    seed/seed.sql                  # sports + demo users/events
    bicep/main.bicep               # App Service + Postgres + App Configuration
    bicep/params.dev.json
  backend/
    src/
      SportMeet.Api/               # controllers, middleware, DI composition root
      SportMeet.Application/       # services (use cases), DTOs, interfaces, validators
      SportMeet.Domain/            # entities, enums, value objects, domain rules
      SportMeet.Infrastructure/    # DbContext, repositories, migrations, Firebase validator, Places client
    tests/
      SportMeet.UnitTests/         # xUnit + FluentAssertions + NSubstitute
      SportMeet.IntegrationTests/  # xUnit + Testcontainers.PostgreSql + WebApplicationFactory
  frontend/
    package.json
    next.config.ts
    tailwind.config.ts
    .env.local
    src/
      app/                         # App Router pages
      components/{ui,events,map,profile}/
      lib/                         # api client, firebase client, auth context, format utils
      types/                       # generated mirrors of API DTOs
      hooks/
```

Dependency rule: `Api -> Application -> Domain`, `Api -> Infrastructure -> Application/Domain`.
`Application` and `Domain` carry **no** EF/Npgsql/Firebase references (interfaces only). That is what makes the
unit tests fast and the later Firebase-to-Identity swap cheap.

---

## 2. Domain Model & Database (Phase 2)

Your schema, kept close to the spec, with seven production-grade changes marked with an asterisk.

### users
| column | type | notes |
|---|---|---|
| id | uuid PK default `gen_random_uuid()` | |
| auth_uid | text unique | Firebase UID; maps external identity to local row |
| name | text not null | |
| email | text unique not null | lower-cased on write |
| photo_url | text null | |
| bio | text null | |
| created_at | timestamptz default now() | always `timestamptz`, never bare `timestamp` |

### sports + user_sports (replaces a text[] of interests on users)
`sports(id serial PK, name text unique, slug text unique, icon text)` seeded with Badminton, Basketball, Running,
Soccer, Tennis, Swimming, Cricket, Volleyball, Table Tennis, Cycling.
`user_sports(user_id uuid, sport_id int, PK(user_id, sport_id))`.
Why: the filter dropdown, the create-event dropdown, the profile chips and (later) AI suggestions all need one
controlled vocabulary, not free text per row.

### events
| column | type | notes |
|---|---|---|
| id | uuid PK | |
| host_id | uuid FK -> users.id (restrict) | |
| title | text not null | max 80 chars |
| description | text null | max 2000 chars |
| sport_id | int FK -> sports.id | FK instead of free-text `SportType` |
| venue_name | text not null | Google Places display name |
| address | text null | Places formatted address |
| place_id | text null | Places place_id, lets us re-resolve server-side |
| lat | double precision not null | CHECK (-90..90) |
| lng | double precision not null | CHECK (-180..180) |
| timezone | text not null default 'Australia/Melbourne' | IANA id; see section 5 |
| start_at | timestamptz not null | |
| end_at | timestamptz not null | CHECK (end_at > start_at) |
| max_participants | int not null | CHECK (2..100) |
| skill_level | text not null | CHECK IN ('Beginner','Intermediate','Advanced') |
| cost | numeric(6,2) null | CHECK (cost >= 0) — `numeric`, never float for money |
| status | text not null default 'scheduled' | CHECK IN ('scheduled','cancelled','completed') |
| created_at / updated_at | timestamptz | |

### event_participants
`(event_id uuid, user_id uuid, joined_at timestamptz default now(), PRIMARY KEY(event_id, user_id))`,
both FKs cascade. The composite PK is what makes a double-join physically impossible.

### event_messages (defined now, built post-MVP)
`(id uuid PK, event_id uuid, user_id uuid, message text, sent_at timestamptz)` + index on `(event_id, sent_at)`.

### CurrentParticipants is derived, not stored
The spec keeps `CurrentParticipants INT` on the row. Two concurrent joins can both read `2` and both write `3`,
producing a 4/3 event. Instead the count is `COUNT(event_participants WHERE event_id = ?)`, surfaced through a view
so list endpoints stay a single round-trip:

```sql
CREATE VIEW v_event_feed AS
SELECT e.*, s.name AS sport_name, s.icon AS sport_icon, u.name AS host_name,
       (SELECT COUNT(*) FROM event_participants p WHERE p.event_id = e.id)::int AS current_participants
FROM events e
JOIN sports s ON s.id = e.sport_id
JOIN users  u ON u.id = e.host_id;
```

The write path is still guarded by a locked transaction (section 3).

### Distances without PostGIS (MVP)
Haversine in SQL, translated by EF Core from a registered db function, plus a coarse lat/lng bounding-box
predicate so the planner can still use an index:

```sql
-- 6371 * acos( cos(radians(:lat1)) * cos(radians(lat)) *
--              cos(radians(lng) - radians(:lng1)) +
--              sin(radians(:lat1)) * sin(radians(lat)) )
-- clamp the acos argument with LEAST(1, GREATEST(-1, ...)) so identical points do not yield NaN
```

Mapped with `modelBuilder.HasDbFunction(() => AppDbFunctions.HaversineKm(default, default, default, default))`
and `HasTranslation(...)`. PostGIS (`<->`, `ST_DWithin`, GiST index) is the upgrade once radius filtering shows up
in `pg_stat_statements`.

### Migrations
`dotnet ef migrations add Init -p src/SportMeet.Infrastructure -s src/SportMeet.Api`, one migration per phase,
never hand-edited after review. `dotnet ef database update` locally; CI produces
`dotnet ef migrations script --idempotent` as an artifact and the deploy step applies it (section 8).

---

## 3. Backend Architecture (Phase 3)

Thin layered shell, vertical slices: controllers stay under ~10 lines, one service method per use case.

### Projects & key packages
| project | packages |
|---|---|
| SportMeet.Domain | none |
| SportMeet.Application | FluentValidation.DependencyInjectionExtensions, NodaTime |
| SportMeet.Infrastructure | Npgsql.EntityFrameworkCore.PostgreSQL, EFCore.NamingConventions, Microsoft.EntityFrameworkCore.Design, FirebaseAdmin, HttpClient for Places |
| SportMeet.Api | Swashbuckle.AspNetCore, Serilog.AspNetCore, Serilog.Sinks.Console, Microsoft.AspNetCore.Authentication.JwtBearer |
| tests | xunit, FluentAssertions, NSubstitute, Microsoft.AspNetCore.Mvc.Testing, Testcontainers.PostgreSql |

### Endpoints (final shape)
| verb + route | auth | purpose | shape |
|---|---|---|---|
| `POST /api/auth/register` | anon | upsert local user after Firebase signup | `{firebaseUid,name,email}` -> `200 UserDto` |
| `POST /api/auth/login` | anon | verify Firebase ID token, upsert user, issue our access token | `{idToken}` -> `200 {token, user}` |
| `GET /api/events` | anon | browse + filter | query params -> `PagedResult<EventListItemDto>` |
| `GET /api/events/{id}` | anon | detail + participants + isHost/isJoined | -> `EventDetailDto` |
| `POST /api/events` | user | create | `CreateEventDto` -> `201` + `Location` header |
| `PUT /api/events/{id}` | host | edit (cannot lower max below current count) | `UpdateEventDto` -> `204` |
| `DELETE /api/events/{id}` | host | soft-cancel (`status=cancelled`) for future events | -> `204` |
| `POST /api/events/{id}/join` | user | atomic join | -> `200 {currentParticipants}` / `409 EventFull` |
| `POST /api/events/{id}/leave` | user | leave; host leaving cancels the event | -> `200` |
| `GET /api/me/events` | user | hosting + joined + past | -> `MyEventsDto` |
| `GET /api/profile` | user | own profile incl. sports | -> `ProfileDto` |
| `PUT /api/profile` | user | name, bio, photoUrl, sports[] (replace semantics) | -> `200 ProfileDto` |
| `GET /api/sports` | anon | dropdown data | -> `SportDto[]` |
| `GET /api/geo/search?q=` | user | Places text-search proxy (keeps the key server-side) | -> `VenueDto[]` |
| `GET /healthz` | anon | `DbContext.CanConnectAsync` | -> `200` |

`GET /api/events` query contract — the whole filter UI maps onto exactly this:

```
?sport=badminton&lat=-37.83&lng=145.12&radiusKm=10&from=&to=&skill=Intermediate
&onlyAvailable=true&search=badminton&page=1&pageSize=20&sort=startAt|distance
```

Defaults: `from = now()`, `radiusKm = 25`, `pageSize = 20` (max 50), `sort = startAt`, 1-based `page`.
OFFSET paging is an accepted MVP shortcut; keyset paging is the named upgrade path.

### Service interfaces (Application)
```csharp
public interface IEventService
{
    Task<PagedResult<EventListItemDto>> BrowseAsync(EventQuery q, CancellationToken ct);
    Task<EventDetailDto>                GetAsync(Guid id, CurrentUserActor actor, CancellationToken ct);
    Task<EventDetailDto>                CreateAsync(CreateEventDto dto, Guid userId, CancellationToken ct);
    Task                                  UpdateAsync(Guid id, UpdateEventDto dto, Guid userId, CancellationToken ct);
    Task                                  CancelAsync(Guid id, Guid userId, CancellationToken ct);
    Task<JoinResult>                    JoinAsync(Guid eventId, Guid userId, CancellationToken ct);
    Task                                  LeaveAsync(Guid eventId, Guid userId, CancellationToken ct);
    Task<MyEventsDto>                   GetMyEventsAsync(Guid userId, CancellationToken ct);
}

public interface IUserService
{
    Task<UserDto>    UpsertFromAuthAsync(ExternalUserPayload p, CancellationToken ct);
    Task<ProfileDto> GetProfileAsync(Guid userId, CancellationToken ct);
    Task<ProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto, CancellationToken ct);
}

public interface ICurrentUser { Guid? UserId { get; } string? Email { get; } bool IsAuthenticated { get; } }
public interface IVenueSearchService { Task<IReadOnlyList<VenueDto>> SearchAsync(string q, CancellationToken ct); }
```

### Repositories & DbContext
`IEventRepository` (Browse by `EventQuery` over `v_event_feed`, GetByIdWithParticipants, Add/Update/Remove),
`IParticipantRepository` (Add/Remove/CountFor/ExistsFor), `IUserRepository`, `ISportRepository`.
`AppDbContext` lives in Infrastructure with a singleton `NpgsqlDataSource`, `UseSnakeCaseNamingConvention()`
(so C# `Title` maps to `title`), and enums (`SkillLevel`, `EventStatus`) stored as strings via
`HasConversion<string>()` — readable in `psql`, no magic integers.

### Join concurrency (the one place correctness really matters)
```csharp
await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
// 1. lock the event row            -> SELECT ... FOR UPDATE (FromSqlRaw / ExecuteUpdate guard)
// 2. count participants;           count >= max  -> throw EventFullException      -> 409
// 3. already joined?               -> throw AlreadyJoinedException                -> 409
// 4. event.start_at < now?         -> throw EventAlreadyStartedException          -> 409
// 5. insert participant            (composite PK blocks duplicates even on retry)
// 6. commit
```
Verified by an integration test that fires `Task.WhenAll` of 5 joins at a 2-spot event and asserts exactly 2 succeed
and the final count is 2 — not 3, not 5.

### Validation & error contract
FluentValidation validators per DTO, auto-registered with `AddValidatorsFromAssembly`. One action filter converts
model state into RFC 9457 `ProblemDetails`. Exception middleware maps `NotFoundException -> 404`,
`ForbiddenException -> 403`, `DomainRuleException -> 409`, `ValidationException -> 422`. Production never returns
stack traces; Serilog logs `TraceId`, `UserId`, route, elapsed ms. All error responses share one shape so the
frontend has exactly one error-handling branch.

### Auth abstraction (so Firebase stays swappable)
```csharp
IExternalTokenValidator  // Firebase: FirebaseAuth.VerifyIdTokenAsync  |  later: IdentityTokenValidator
ITokenService            // issues our own HS256 access token: sub = users.id, uid = Firebase UID
ICurrentUser             // reads claims; identical for both providers
```
`Program.cs` selects the implementation from config `Auth:Provider = "Firebase" | "Jwt"`. Our access token:
HS256, issuer `sportmeet-api`, audience `sportmeet-web`, 60-minute lifetime. Refresh strategy for MVP: the client
calls `POST /api/auth/login` again with a fresh Firebase ID token (no refresh-token table needed). Firebase Admin
credentials reach Azure via an App Setting holding the service-account JSON (or a mounted file) — never committed.
Authorization policy: `RequireUser`. Ownership checks live in services, not attributes, so they are unit-testable.

### Config & DI
`appsettings.json` sections: `ConnectionStrings:Default`, `Auth`, `GoogleMaps`, `Cors:Origins`, `Paging`, `Serilog`.
Typed `IOptions<>` records, `AddApplication()` / `AddInfrastructure()` extension methods, scoped `DbContext`,
singleton `NpgsqlDataSource`, controllers with `JsonStringEnumConverter` + camelCase. CORS policy `web` from config
(dev: `http://localhost:3000`), HTTPS redirect + HSTS in Production only. Rate limiting on
`POST /api/events/{id}/join` via `AddRateLimiter` (partition key = user id, 10/min) as free spam insurance.

---

## 4. Frontend (Phase 4)

Next.js 15 App Router, TypeScript, Tailwind. Firebase auth is client-side for MVP, so event data is fetched from
client components with **SWR** (cache + revalidate on focus/`mutate` after join). Forms: `react-hook-form` + `zod`
(+ `@hookform/resolvers`).

### Pages
| route | purpose | key pieces |
|---|---|---|
| `/` | browse + map — the money screen | `EventFilterBar`, `EventCardList`, `EventMap`, `useEvents(filters)` |
| `/events/[id]` | venue map, participant avatars, Join/Leave, host edit/cancel | `EventDetail`, `ParticipantList`, `JoinButton` |
| `/create` | create form (auth required) | `EventForm`, `VenuePicker` (Places search) |
| `/events/[id]/edit` | same form, edit mode | `EventForm mode="edit"` |
| `/my-events` | tabs Hosting / Joined / Past | `useMyEvents()` |
| `/profile` | name, bio, photo, sport chips | `ProfileForm`, `AvatarUploader` |
| `/login`, `/register` | email/password + Google button | `useAuth()` |

### Data layer (`src/lib`)
- `api.ts`: typed `fetch` wrapper — injects `Authorization: Bearer <our access token>`, converts `ProblemDetails`
  into a typed `ApiError {status,title,errors}`, `AbortController` tied to route changes, 10s timeout, single retry
  on network error for idempotent GETs.
- `auth.tsx`: lazy `initializeApp`, `onAuthStateChanged` -> `AuthContext {user, token, loading, login, register, logout}`;
  on first auth of a session it calls `POST /api/auth/login` so the DB row exists before any page renders.
- `types/api.ts`: mirrors the DTOs. Export `openapi.json` from the API in Development and generate with
  `npx openapi-typescript` under `npm run gen:api` — removes an entire class of drift bugs.
- `format.ts`: `formatDistanceKm`, `formatWhen` ("Tonight 6PM", "Mon 12 Oct 6PM"), `sportIcon` emoji map — these
  produce the exact strings in your mockups, and they are the easiest frontend unit tests to write.

### Map (`src/components/map`)
`@vis.gl/react-google-maps` + `useJsApiLoader`. `AdvancedMarker` per event with a sport-emoji label, `InfoWindow`
showing `Badminton · 2/4 Players · 6PM Tonight · [Join]`; clicking a pin highlights and scrolls to the matching list
card and vice versa. Initial centre: browser geolocation, fallback Melbourne CBD `(-37.8136, 144.9631)`. Debounce
`center_changed` 600ms and add a "Search this area" button so we do not hammer the API. Add
`@googlemaps/markerclusterer` once a view can hold more than ~50 pins.

### Filters
URL-driven (`useSearchParams`) so a filtered view is shareable and the back button works, e.g.
`/?sport=badminton&radiusKm=10&from=thisWeek&onlyAvailable=1`. Controls: sport chips, radius select (5/10/25/50 km),
when select (Today / This week / This month / All), skill select, "Not full" toggle, text search. The server is the
single source of filtering truth — no client-side re-filtering, so list and map can never disagree.

### Styling & a11y
Tailwind + `clsx` with hand-rolled `Button`, `Card`, `Chip`, `Select`, `Field`, `Skeleton` primitives (no heavy
component library — the point is to learn the primitives). Mobile first: `/` is a vertical card list with a
collapsible map sheet below 768px. Semantic HTML, visible focus rings, `aria-live="polite"` on participant counts,
44px tap targets, loading skeletons on every fetch, real empty states ("No badminton within 10km this week —
create the first one").

---

## 5. Google Maps Integration (Phase 5)

Two keys, deliberately separate:

1. **Browser key** (`NEXT_PUBLIC_GOOGLE_MAPS_API_KEY`) — Maps JavaScript API only, HTTP-referrer restricted to
   `localhost:3000` and the Vercel domain.
2. **Server key** (`GoogleMaps:ApiKey` in Azure) — Places Text Search / Details, never shipped to the client.

Venue search is proxied through `GET /api/geo/search` (one endpoint buys key safety). On create we store
`place_id`, `venue_name`, `address`, `lat`, `lng`, and **re-resolve `place_id` server-side** with Places Details so a
hand-crafted request cannot plant an event in Port Phillip Bay; the lat/lng range CHECKs are the second line of
defence. Billing guardrails: quota caps in GCP (100 req/min per key), minimal `X-Goog-FieldMask` on the new Places
API, 24h `IMemoryCache` keyed by `place_id`.

Time zones — the #2 bug source in event apps after concurrency: the form collects a local wall time
(`2026-10-05 18:00`) plus the venue's IANA zone (`Australia/Melbourne`); the API uses NodaTime (`DateTimeZoneProviders.Tzdb`)
to produce `Instant`s, and `start_at`/`end_at` are stored as `timestamptz`. Include a unit test on the
2026-10-04 AEST->AEDT switch. Frontend renders in the viewer's locale via `Intl.DateTimeFormat`.

---

## 6. Testing Strategy

| layer | what | target |
|---|---|---|
| Domain unit | `Event.CanJoin/CanEdit/CanCancel`, Haversine math, time-zone conversions incl. DST | fast, no DI |
| Application unit | every validator; `JoinAsync` for full / double-join / started / not-found; `UpdateAsync` max-below-current | ~25 tests, >=80% line coverage gate on `SportMeet.Application` |
| Integration | `WebApplicationFactory` + Testcontainers Postgres: register -> login -> create -> browse -> join (3rd gets 409) -> leave -> my-events -> profile; each filter (radius/skill/available/date/search); 401 on protected routes; 403 for non-host edit | ~15 tests, includes the `Task.WhenAll` concurrency test |
| Frontend | Vitest + RTL: `format.ts`, filter -> query-string mapping, `JoinButton` states (idle/joined/full/error) | ~20 tests |
| E2E | one Playwright happy path: register A -> create -> register B -> search -> join -> see 3/4 (Week 4, run against staging) | 1 spec |
| Migration | CI job runs `dotnet ef database update` on a throwaway container then `seed.sql` smoke | 1 job |

`dotnet test` and `npm test` both green before any merge; CI blocks red.

---

## 7. Observability & Ops (cheap now, painful later)

- Serilog JSON to stdout (App Service streams it); App Insights sink once enabled.
- `GET /healthz` (DB reachable) wired to the App Service health-check probe and the compose healthcheck.
- Correlation: `TraceId` echoed in `ProblemDetails.extensions`; frontend sends `X-Request-Id`.
- Bicep (`infra/bicep/main.bicep`) provisions App Service (Linux, `DOTNET_VERSION=10.0`, health check path `/healthz`),
  Flexible Server `B1ms` with TLS 1.2 + `require_secure_transport=on` + **VNet integration, never `0.0.0.0/0`**,
  App Configuration, App Insights. Deploy with `az deployment group create -g sportmeet-rg -f main.bicep -p params.dev.json`.
- `README.md` runbook: every command, one prerequisite list (`docker compose up -d`, `dotnet run`, `npm run dev`).

---

## 8. Build Order (your weeks 1-5, each with a hard exit criterion)

### Week 1 — foundations + auth
git init, `.gitignore`/`.editorconfig`/`Directory.Build.props`/solution; 4 src + 2 test projects; `docker-compose.yml`
(Postgres 16, port 5432, named volume, healthcheck); EF Core + snake_case + entities + `Init` migration + `sports`
seed migration; CORS/Serilog/healthz/ProblemDetails; Firebase project + providers; `IExternalTokenValidator`,
`ITokenService`, `RequireUser`; `POST /api/auth/register`, `POST /api/auth/login`; `GET /api/sports`; Next.js +
Tailwind + `AuthContext` + login/register pages; `ci.yml` (restore, build, unit tests, `lint`, `typecheck`, `test`).

**Exit:** `docker compose up -d && dotnet run` -> `/healthz` green; a real browser user signs in with Google and a
`users` row appears; a protected endpoint returns their profile; `dotnet test` and `npm test` green; PR cannot merge red.

**Commands:** `dotnet new sln -n SportMeet` · `dotnet new webapi -n SportMeet.Api -o backend/src/SportMeet.Api --no-https` ·
`dotnet new classlib -n SportMeet.Domain|Application|Infrastructure` · `dotnet new xunit -n SportMeet.UnitTests|IntegrationTests` ·
`dotnet sln add ...` · `dotnet ef migrations add Init -p ... -s ...` · `npx create-next-app@latest frontend --ts --tailwind --app --eslint`.

### Week 2 — create / browse / join / leave
`CreateEventDto` + validator; `POST /api/events`; `GET /api/geo/search` proxy; `v_event_feed` migration;
`GET /api/events` with the full query contract; `GET /api/events/{id}`; Join/Leave with the locked transaction and
`409 EventFullProblemDetails`; rate limiter on join; `GET /api/me/events`; integration tests including the
concurrency test; frontend `/create` (react-hook-form + zod), `/` card list with Join + optimistic count, `/my-events`.

**Exit:** two browser profiles — A creates "Badminton, Mon 6-8PM, Glen Waverley Badminton Centre, max 4,
Intermediate, $15"; B sees "2/4 Players · Tonight 6PM", joins, sees "3/4"; with max 3 the third join returns 409 and
the button disables; A cancels and the event disappears from B's upcoming list.

### Week 3 — map, filters, profiles
Split-view map page, emoji markers, InfoWindow join, clustering; URL-driven filters including `radiusKm` via
Haversine and "Not full"; `PUT /api/profile` with `sports[]`; avatar upload to Azure Blob (SAS URL in `photo_url`)
with 5MB JPEG/PNG whitelist; avatar in the nav; skill/cost/status rendering; empty + error states; Lighthouse pass;
Playwright e2e.

**Exit:** filtering Badminton / 10km / this week / not full returns exactly the right card, and the map pin for it
opens an info window whose Join button works; profile sport chips persist across reload; no console errors;
Lighthouse performance >= 85 and accessibility >= 90.

### Week 4 — deploy + CI/CD
Bicep environment; `dotnet publish` zip deploy to App Service (Linux, `DOTNET_VERSION=10.0`); migrations applied on
deploy via idempotent SQL script or `dotnet ef database update` from a runner with VNet access; Vercel import with
`NEXT_PUBLIC_*` env vars; `deploy-api.yml` (build -> test -> publish -> `azure/webapps-deploy`) gated on `main`;
`openapi.json` published as a CI artifact consumed by `npm run gen:api`; secrets to App Settings / Key Vault +
GitHub secrets; custom domain + HTTPS; smoke suite against `https://api.<domain>`; `pg_dump` backup + restore drill.

**Exit:** merge to `main` puts the API on Azure and the web app on Vercel, DB migrated, the Playwright spec passes
against production URLs, and rollback is a one-click App Service deployment swap.

### Week 5+ — enhancements, in this order
1. **SignalR** `/hubs/event/{id}` pushing `ParticipantCountChanged` so `2/4 -> 3/4` without refresh (reuse `JoinResult`).
2. **Chat** on the same hub (table already exists); history over REST.
3. **Ratings** `event_ratings(event_id,user_id,rating,comment)` with composite PK, only for `completed` events the user attended.
4. **Waitlist** `position int` + promotion on leave, ideally `DEFERRABLE UNIQUE(event_id, position)`.
5. **Recurring events** `event_series` + materialised occurrences (unique `(series_id, start_at)`).
6. **Follows / feed**, then **AI suggestions** using `user_sports` + distance + co-attendance as features.
7. **Push via FCM** (service worker + `webpush` keys) — after SignalR, so notification content is already modelled.

---

## 9. Key Decisions & Trade-offs (so future-you doesn't relitigate them)

1. **Derived participant count + row lock** over a denormalised counter: correctness first, and at <=100 participants
   per event the `COUNT` is free. Revisit with a counter column if `v_event_feed` ever shows up slow.
2. **Firebase Auth behind `IExternalTokenValidator`/`ITokenService`** — fastest real login with Google/Apple today,
   and the swap to ASP.NET Core Identity later is one new implementation plus a config flag.
3. **`timestamptz` + NodaTime + stored IANA zone** — non-negotiable for a time-based meetup app.
4. **Places proxied server-side** — key safety plus the ability to normalise/validate coordinates.
5. **Haversine now, PostGIS later** — no spatial dependency in MVP, query isolated in one translation.
6. **Monorepo** — one PR shows migration + endpoint + UI, which is how you learn to review a full vertical slice,
   and CI/CD stays shared.
7. **Accepted MVP shortcuts with named upgrade paths:** OFFSET paging -> keyset; client-side Firebase auth ->
   BFF cookie session; REST-only -> SignalR; no Soft-delete -> `status` column already handles the visible case.

## 10. Top Risks & Mitigations

| risk | mitigation |
|---|---|
| Firebase Admin creds missing at deploy, so login 500s only in production | dedicated `/healthz/auth` check + Week 1 exit criterion re-run against a staging slot in Week 4 |
| Melbourne DST off-by-one in `start_at` | NodaTime end to end + unit test on the 2026-10-04 AEST->AEDT boundary |
| Google Maps key leaked from the bundle | referrer + per-API restrictions, quota caps, server-side Places, weekly key usage review |
| Double join / overbooking under load | composite PK + locked transaction + `Task.WhenAll` integration test |
| Postgres firewall locks out the deploy runner | VNet integration, never `0.0.0.0/0`; run migrations from inside the VNet or via App Service SSH |
| Maps/Places bill surprise | quota caps on both keys, minimal field masks, `IMemoryCache`, Vercel analytics alerts |
| Solo-project bus factor | this plan + `README.md` runbook; `docker compose up` is the only local prerequisite besides Node/Docker |
| Repo sits inside a synced OneDrive folder | move the code to `C:\Dev\Event_Manager`, or pin `node_modules`/`bin`/`obj` locally and pause sync during builds (section 11) |

---


## 11. Local Environment Readiness (audited 2026-09-21)

Machine: Windows 11 Pro 10.0.26200, 15.5 GB RAM, winget v1.29.290 available.

| Component | Status | Action |
|---|---|---|
| .NET SDK 10.0.400 | present | none |
| git 2.55.0 | present | set `core.longpaths true`, `core.autocrlf input` before first commit |
| winget v1.29.290 | present | use it for all installs below |
| Node.js | installed (v24.11.0, LTS line "Krypton"; newest is 24.21.0 — same major, safe to stay, no nvm) | optionally `winget install OpenJS.NodeJS.LTS` later |
| PowerShell execution policy | was `Restricted`, blocked `npm.ps1` | `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` (user action needed) |
| Docker Desktop CLI | installed (29.8.0, Compose v5.5.1) | none |
| Docker Linux engine / daemon | **not running** | needs WSL2 (below) + Docker Desktop started + terms accepted |
| WSL2 | **missing** ("not installed") | required by Docker Desktop: `wsl --install -d Ubuntu-24.04` then reboot |
| psql / pg_dump CLI | missing | not needed — run clients through `docker compose exec` |
| Azure CLI / Bicep | missing | defer to Week 4 (`winget install Microsoft.AzureCLI`, Bicep ships with it) |
| Google Cloud CLI | missing | not needed (Maps/Firebase keys only, configured in consoles) |
| VS Code extensions | unchecked | `dbaeumer.vscode-eslint`, `esbenp.prettier-vscode`, `bradlc.vscode-tailwindcss`, `ms-dotnettools.csdevkit`, `ms-azuretools.vscode-bicep`, `rangav.vscode-thunder-client` |

Install commands (run in an elevated PowerShell):

```powershell
winget install OpenJS.NodeJS.LTS      # resolves to latest v24 LTS MSI, adds node+npm to PATH
winget install Docker.DockerDesktop   # offers WSL2 backend during setup
wsl --install -d Ubuntu-24.04         # if Docker setup did not do it; requires reboot
```

Do **not** install Postgres natively on Windows — the container *is* the database, matching the Azure engine version.

### Working-directory decision (RESOLVED 2026-09-21)

**Canonical repo path: `C:\Dev\Event_Manager`.** All code, migrations, `node_modules` and the Docker compose file
live there. Do not open or edit the old location.

The project originally sat at `...OneDrive - SPX Technologies\Desktop\David\Event_Manager`, which is unsafe for a
real web app for two reasons:
1. **OneDrive file locking / on-demand hydration** — `npm install` writes tens of thousands of small files and
   OneDrive's sync can hold or dehydrate them. Symptoms: `EBUSY`, `EPERM`, `ENOENT` in `node_modules`, and EF
   migrations that fail nondeterministically.
2. **Path length + spaces** — `node_modules` nests deeper than MAX_PATH (260); nested `.bin`/`.pnpm` paths under a
   55-character parent folder blow past it, and git then reports "Filename too long".

Completed actions:
```powershell
git config --global core.longpaths true          # done  (verified: git config --get core.longpaths -> true)
git config --global core.autocrlf input          # done
New-Item -ItemType Directory C:\Dev              # done
Copy-Item ...\David\Event_Manager C:\Dev\Event_Manager -Recurse   # done, byte-verified 34,711 == 34,711
```

Rules that follow from this:
- `C:\Dev` is outside the OneDrive sync root, so nothing there syncs, locks, or dehydrates. No OneDrive settings
  changes are needed.
- The old OneDrive copy is a **frozen backup, not a second checkout**. Rename it
  `Event_Manager_DO_NOT_USE_BACKUP` and never edit it; two editable copies is how a week disappears.
- OneDrive was providing off-site backup for the plan document. Replace it with git (below) rather than leaving
  the code unbacked-up.
```powershell
cd C:\Dev\Event_Manager
git init -b main
git add IMPLEMENTATION_PLAN.md
git commit -m "docs: add MVP implementation plan"
# create the private GitHub repo, then: git remote add origin ... ; git push -u origin main
```

### Definition of ready (run these after installs, before Week 1 starts)
```powershell
node -v; npm -v              # expect v24.x and npm 11.x
dotnet --info | Select-String "10.0"
docker version               # client + server both answer (server = Docker Desktop/WSL2 healthy)
docker compose version
docker run --rm postgres:16-alpine echo ok   # proves image pull + WSL VM storage work
git config --get core.longpaths              # true
```
If `docker version` errors with "failed to connect to the Docker daemon", Docker Desktop is not running or WSL2
did not finish — fix that before scaffolding, because Week 1's very first task is `docker compose up -d`.

### First-run fixes actually hit on this machine (2026-09-21)

Symptom 1 — `node -v` printed but `npm : File C:\Program Files\nodejs\npm.ps1 cannot be loaded because running
scripts is disabled`:
PowerShell's effective policy on this box was `Restricted` (MachinePolicy/UserPolicy both `Undefined`, so it is a
local default, not a corporate lockdown). The MSI ships `npm.ps1`; PowerShell refuses `.ps1` under `Restricted`.
Fix at CurrentUser scope — does not need admin and does not weaken the machine-wide default:
```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned     # answer Y
Get-ExecutionPolicy                                     # RemoteSigned
npm -v
```
`RemoteSigned` runs local scripts and still requires a trusted signature for downloaded ones. Do **not** use
`Bypass`/`Unrestricted`. Fallback without touching policy: call `npm.cmd` instead of `npm`, or run from cmd.exe.

Symptom 2 — `failed to connect to the Docker API at npipe:////./pipe/dockerDesktopLinuxEngine`:
Docker CLI installed (29.8.0) and Compose v5.5.1, but the Linux-engine VM never came up because **WSL2 is not
installed**. `wsl --status` still reports "not installed" after the Docker install, so run it explicitly, reboot,
then start Docker Desktop and wait for "Docker Desktop is running" in the tray before re-testing:
```powershell
wsl --install -d Ubuntu-24.04 --no-launch    # enables VirtualMachinePlatform + installs kernel/distro
# reboot
wsl --update
wsl --status
Start-Process "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"
docker version          # Server section must appear
```
If the Server section is still missing after a healthy WSL: Docker Desktop → Settings → Resources → WSL Integration
(enable the Ubuntu distro), and confirm Hyper-V / "Windows Hypervisor Platform" is on (`OptionalFeatures.exe`).
Also accept the Docker subscription/terms on first launch — the engine does not start until it is accepted.







