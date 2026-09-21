# Event Manager — UI/UX Design Spec

Target: a stranger creates a badminton game at Glen Waverley, another stranger finds it on a map and joins it, in
under 60 seconds and with zero instructions. Every decision below serves that.

- Applies to `frontend/` (Next.js App Router + Tailwind + TypeScript).
- See `../IMPLEMENTATION_PLAN.md` §4 for routes and §3 for the API contract this UI consumes.
- Supporting files here: `design-system.html` (open in a browser — live style guide), `tokens.css` (theme source of truth).

## 0. Design principles (the rules that settle arguments later)

1. **Count is the hero.** `2/4` is the largest number on a card and the only thing that changes live. If "can I
   still join this?" isn't answerable at a glance, the card failed.
2. **Never two sources of truth on screen.** List and map read the same server-filtered array; the URL holds filter
   state. A card and a pin disagreeing is a bug, not a refresh problem.
3. **One primary action per screen.** On `/` it is `Join`. Anything competing with it gets demoted or removed.
4. **Failure is a first-class state.** Every fetch renders loading, empty, error and success. `409 EventFull` is a
   normal outcome, not an error toast.
5. **Optimistic, then honest.** The count updates on click; a `409` rolls it back and says so in the same spot.
6. **Times and distances are formatted once**, in `lib/format.ts`. No component builds its own date string.
7. **44px targets, visible focus, never colour-only meaning.** Sport is emoji *and* label.

## 1. Brand & tone

Working name **Event Manager** (`EVNT` as the mark). Melbourne social sport. Copy is plain, warm, verb-first:
`Join`, `Leave event`, `Create event` — never `Submit`/`Proceed`/`Are you sure you wish to continue?`. Empty states
propose the next action (`No badminton within 10 km this week — create the first one`). Error copy names the thing,
not the system: `This event is full`, not `Request failed with status 409`.

## 2. Colour tokens

Neutral-first with one accent, so sport emojis and the map carry the colour. `oklch` throughout — perceptually
uniform, so 500 and 600 really are one step apart. Dark theme ships from day one.

| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-bg` | `oklch(0.99 0.002 250)` | `oklch(0.17 0.012 260)` | page background |
| `--color-surface` | `oklch(1 0 0)` | `oklch(0.22 0.014 260)` | cards, sheets, popovers |
| `--color-surface-2` | `oklch(0.97 0.004 250)` | `oklch(0.27 0.016 260)` | insets, resting chips |
| `--color-fg` | `oklch(0.24 0.02 255)` | `oklch(0.95 0.006 250)` | body text |
| `--color-fg-muted` | `oklch(0.53 0.02 255)` | `oklch(0.7 0.015 250)` | metadata, secondary |
| `--color-border` | `oklch(0.9 0.006 250)` | `oklch(0.34 0.02 260)` | hairlines, inputs |
| `--color-brand-600` | `oklch(0.55 0.17 155)` | `oklch(0.72 0.16 155)` | primary button, active chip, links |
| `--color-brand-fg` | `oklch(1 0 0)` | `oklch(0.18 0.02 155)` | text on brand |
| `--color-brand-tint` | `oklch(0.95 0.04 155)` | `oklch(0.28 0.06 155)` | selected row, focus halo |
| `--color-warn` | `oklch(0.72 0.16 80)` | `oklch(0.78 0.15 80)` | "1 spot left" |
| `--color-danger` | `oklch(0.58 0.2 25)` | `oklch(0.66 0.19 25)` | full, cancel, destructive |
| `--color-info` | `oklch(0.6 0.14 250)` | `oklch(0.72 0.13 250)` | cancelled badge |

Skill level stays neutral (surface-2 + muted) — it describes, it doesn't warn. Availability is the only semantic
colour: `>=2` open = brand, `1` open = warn, `0` open = danger.

## 3. Type scale

`Inter` variable, self-hosted via `next/font` (`display: swap`). Tabular numerals on every count and time so
`2/4 -> 10/12` doesn't jitter.

| Token | Size/line | Weight | Where |
|---|---|---|---|
| `text-hero` | 30/36 | 700 | the only two H1s: `/`, `/create` |
| `text-h2` | 22/28 | 700 | detail-page title |
| `text-h3` | 17/24 | 600 | card title, section head |
| `text-body` | 15/22 | 400 | default (14px floor, 15px default) |
| `text-meta` | 13/18 | 500 | distance, cost, list counts |
| `text-count` | 20/24 | 700 `tabular-nums` | the `2/4` |
| `text-micro` | 11/14 | 600 uppercase `.04em` | badges only |

## 4. Spacing, radius, elevation

4px base: `--space-1..12` = 4, 8, 12, 16, 20, 24, 32, 40, 48. Density rule — 12px inside a control, 16px between
related rows, 24px between unrelated blocks.

Radius: `sm 6` (inputs, chips), `md 12` (cards, buttons), `lg 18` (sheets, modals), `full` (avatars, sport chips).
One card never mixes two radii.

Elevation is rare: `shadow-0` hairline (default for cards), `shadow-1 0 1px 2px oklch(0 0 0/.06)` (sticky bars),
`shadow-2 0 8px 24px oklch(0 0 0/.12)` (popovers, InfoWindow, modals). No nested shadows.

Focus ring is an a11y contract, not decoration: global `:focus-visible { outline: 2px solid var(--color-brand-600);
outline-offset: 2px }`, and never `outline: none`.

## 5. Motion

Two durations, one curve: `--dur-1 120ms` (hover/press), `--dur-2 200ms` (sheet, chip, count change),
`--ease cubic-bezier(.2,0,0,1)`. Count changes nudge 4px vertically so `2/4 -> 3/4` registers without animation
being the point. `prefers-reduced-motion` collapses everything to ~0. We never animate map panning — Google owns it.

## 6. Layout & breakpoints

Mobile-first, fully usable at 360px. `sm 640 / md 768 / lg 1024 / xl 1280`.

- Single-column flows (`/create`, `/profile`, `/events/[id]`) sit in `max-w-[680px]`, `px-4`, centred.
- `< md`: `/` = sticky filter bar + card list + a **collapsed map sheet** that expands to 60vh. Detail map = 200px.
- `>= lg`: `/` = `420px` list column + map filling the rest, independently scrollable (`100dvh`).
- Primary CTA: 48px tall, full-width on mobile, in a sticky footer bar with `pb-[env(safe-area-inset-bottom)]`.

## 7. Component states

Skeletons reproduce exact final geometry, so data landing never shifts layout — that is the whole CLS story.

**Button** — `primary | secondary | ghost | danger`, sizes `sm 36 / md 44 / lg 48`. Rest, hover, `active`
(`translateY(1px)`), `focus-visible`, `loading` (spinner replaces label **at held width**, `aria-busy`), `disabled`
(surface-2 + muted fg, not opacity on brand). A width-changing loading state makes the sticky footer jump.

**EventCard** — sport row (`🏸 Badminton` + skill chip right), `text-h3` title, `text-meta` venue · time, footer
row `text-count 2/4` + `3 km away` + CTA. Variants: `open`, `one-spot` (count in warn, `1 spot left`), `full`
(disabled `Full`, count in danger), `joined` (`Joined ✓` secondary, brand-tinted footer), `mine` (3px brand leading
border + `You're hosting`), `cancelled` (muted whole card, info badge, no CTA), `past` (single line), `skeleton`,
hover (`shadow-1` + `translateY(-1px)` — never `scale`, it reflows the grid). `<Link>` wraps the card; the CTA calls
`stopPropagation`.

**JoinButton** — a state machine, not a button with a handler:
`idle → joining(spinner, width held) → joined`, plus `full | starting | error`. On `409` it rolls the optimistic
count back, switches to `full`, and writes `This event just filled up` inline in `text-meta` with
`aria-live="polite"` announcing `3 of 4 spots taken`. On `5xx`/network it restores the count and offers `Try again`.
It never disappears — a control vanishing under a tap reads as a broken app.

**Chip** — `rest / hover / selected(tint + brand text + check glyph) / disabled`, 34px, radius-full. Selection adds
a check, not only a hue, so it survives colour-blind simulation.

**Field** — label **above** the control (placeholders vanish exactly when needed), control, then helper or error in
danger. Errors set `aria-invalid` + `aria-describedby`, appear on `blur`/submit, never mid-first-word. Pairs
(date+time, max+cost) go two-up at `sm`.

**Select / DateInput / TimeInput** — native controls *styled*, not custom widgets: mobile's native pickers beat
anything we write. `appearance-none` + caret for Select.

**FilterBar** — sticky `z-20`, gains `shadow-1` on scroll. Mobile: horizontally scrolling chips + a `Filters`
button opening a bottom sheet with Apply/Reset; the button carries the active count (`Filters · 3`) because chips
scroll off-screen. Desktop: inline row. Writes straight to the URL.

**SearchInput** — 300ms debounce, `cmd+/` focus, `Escape` clear, `type="search"`, never fetches per keystroke.

**Avatar** — 28/36/56, `object-cover`, fallback initials on surface-2 (never a broken-image icon). Participant list
is an overlapping stack, max 4 then `+3`.

**Badge / Skeleton / Toast / Modal / BottomSheet / EmptyState / ErrorState** — `EmptyState` = one sentence + one
primary action, no illustration. `ErrorState` = what failed, `Try again`, technical detail inside a `<details>`.
Toast: top-centre mobile / bottom-right desktop, max 2, transient confirmations only — persistent facts (full,
cancelled) live on the card.

**EventMap** — `AdvancedMarker` with sport emoji + `2/4`; `InfoWindow` is a mini card (`text-h3`, count, time,
Join). Selected pin scales 1.15 + `shadow-2`. List selection and pin selection drive the *same* `selectedEventId`
in the parent, so both directions behave identically. `Search this area` pill appears bottom-centre only after the
centre moves >0.5 km. Clusters show counts and zoom in on click. `aria-label="Map of events"`; the list is the
accessible alternative, never the reverse.

## 8. Screen specs

**`/` Browse** — `FilterBar`, then `EventCardList | EventMap`. First paint: filter bar + 6 skeletons + map
placeholder at last-known centre. Geolocation denied → silently fall back to Melbourne CBD with a one-line
`Turn on location to sort by distance`; never a permission modal before the user has seen content. Sort defaults to
`startAt`, switches to `distance` once a location exists and a radius is active.

**`/events/[id]`** — title, sport+skill chips, `2 of 4 spots taken`, host row with `Host` badge, description,
full-width venue map, address, sticky footer CTA. Host's `Edit` / `Cancel event` live in a `⋯` menu, destructive
last, confirmed with a modal naming the consequence: `Cancel "Badminton Monday"? 2 people have joined.`

**`/create`** — five groups: **What** (name, sport chips, skill), **When** (date, start, end), **Where**
(VenuePicker), **Who & how much** (max, cost), **Description**. A live preview card pinned top-right at `lg+` is the
cheapest way to teach what the listing will look like. VenuePicker: input → debounced `GET /api/geo/search` → rows
with address + distance → picking fills read-only `venueName/address/lat/lng` chips with a `Change` link. Client
validation mirrors the API's; server `422` field errors land on the matching `Field`. Submit keeps `loading`, then
routes to detail with toast `Event created`.

**`/my-events`** — tabs `Hosting | Joined | Past`, counts in labels (`Joined (2)`). Reuses `EventCard` variants.
`Past` collapses to `🏸 Badminton · Mon 12 Oct` with a reserved `Rate` slot for post-MVP.

**`/profile`** — avatar with camera overlay (upload on select, progress ring, 5MB/format rejected *before* upload),
name, bio with `180/200` counter, sport chips multi-select. Saves on `Save` — silent autosave is surprising.

**`/login` `/register`** — one card, `Continue with Google` above a divider, email/password below. Register asks for
name only; photo and sports are profile's job, and every extra field costs signups. Post-auth we call
`POST /api/auth/login` silently behind a skeleton shell, not a spinner.

## 9. States matrix (this is what makes it feel finished)

| Surface | Loading | Empty | Error | Success | Partial |
|---|---|---|---|---|---|
| `/` list | 6 skeletons | EmptyState echoing active filters + Create CTA | ErrorState + retry, filters preserved | cards | page 2 skeletons appended; filter bar stays live |
| `/` map | grey block + `Loading map…` | "no events" pill over the map, not a blank one | `Map failed to load`, list still fully usable | pins | list ready while map loads is normal |
| JoinButton | spinner, width held | — | rollback + `This event just filled up` | `Joined ✓` + count nudge | 409 and 5xx copy deliberately differ |
| `/create` | button loading, fields editable | — | per-field + top summary link-scrolling to first | redirect + toast | impossible by design — API is one transaction |
| `/profile` | button loading | — | per-field | `Profile saved` | avatar uploads independently of the form |
| `/my-events` | tab skeletons | per-tab copy | retry | cards | one failed tab must not blank the others |

## 10. Accessibility acceptance (checked, not assumed)

Lighthouse a11y `>= 95`; keyboard-only walkthrough of browse → filter → open → join with no focus traps; one `h1`
per page; icons either `aria-hidden` or labelled; `aria-live="polite"` on counts and toasts; contrast `>= 4.5:1`
body and `>= 3:1` UI boundaries in **both** themes; `prefers-reduced-motion` honoured; bottom sheet traps and
restores focus; the list is server-rendered so `/` has content before JS.

## 11. Performance rules ("smooth" is mostly these)

Self-hosted `next/font` Inter (no render-blocking Google request); Maps JS API lazy-loaded via `useJsApiLoader`
only on `/` and `/events/[id]`, after first paint; `next/image` for avatars; `React.lazy` for `EventMap` and the
autocomplete; 300ms debounce + `AbortController` on route change; SWR `revalidateOnFocus` so returning to the tab
refreshes counts without a spinner. Targets: LCP < 2.0s on Vercel mobile throttle, **CLS < 0.05** (skeletons hold
geometry — the reason they're mandatory), INP < 200ms (URL writes wrapped in `startTransition`).

## 12. Naming contract with the backend

`EventListItemDto` → prop `event: EventListItem`. `SkillLevel` arrives as PascalCase strings, lower-cased only for
class lookup. `cost: number | null` renders `Free` when `null` or `0`. `sportIcon` is server-supplied (emoji lives
in the DB seed), with a local fallback map for offline dev. Dates arrive as ISO UTC plus the event's `timezone`;
only `lib/format.ts` converts.

## 13. Definition of done, UI

No screen shows raw JSON, `NaN`, `undefined`, `Invalid Date`, or a spinner older than 400ms without skeletons behind
it. `npm run lint` + `typecheck` + Vitest green. Playwright: register → create → search → Join → sees `3/4`.
Lighthouse Perf ≥ 85, A11y ≥ 95, Best practices ≥ 95, SEO ≥ 90. Both themes screenshot-tested at 360, 768, 1280.


