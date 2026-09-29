# Tags

Events are labelled with free-text tags (`#tennis`, `#club`). These replaced the
fixed sport vocabulary as the browse filter, the search dimension and the create
form's activity picker, so a new activity is a keystroke rather than a type change
plus a redeploy.

## Rules (mirrored in `backend/.../TagNormalizer.cs` and `lib/sports.ts`)

- lowercase, leading `#` stripped, internal whitespace collapsed, trimmed
- 2-25 characters, must contain at least one letter or digit (so `#` alone is not a tag)
- deduplicated first-seen, **max 5 per event**; order is preserved, not sorted
- created implicitly on first use; no curation, no merging, no admin screen
- `#Tennis` and `#tennis` are one tag because they normalize identically *before*
  the ordinal unique index sees them, not because the database folds case

Near-duplicates (`#tennis` vs `#tennisis`) are therefore **not** prevented — that
is the accepted cost of no curation, and the reason popularity counts can split.

## Search and filter

- `?q=` matches title OR venue OR host OR tags (OR, not AND — a searcher cannot
  know which the host used).
- `?tag=` matches one tag exactly. The view stores tags as a comma-joined
  aggregate, so the filter delimits with commas: `?tag=club` does not match an
  event tagged `nightclub` (covered by a live check, not just reasoned about).
- Free-text search over that aggregate IS substring-matched, so `?q=club` does
  find `#nightclub`. That asymmetry is deliberate: substring is right for search,
  exact is right for a filter chip.
- URL param is `?tag=`; `?event=` and `?sport=` still parse so old shared links work.

## Popular tags

`GET /api/tags/popular` counts tags on events that are **scheduled, not cancelled,
and not in the past** — the same baseline as `ApplyFilters`, so a chip on the home
page can never open an empty result set.

**A fresh database returns `[]`.** The demo seed creates no tags (deliberate: tags
are user vocabulary). The chip row renders nothing rather than an empty control,
and the mobile filter sheet shows a one-line explanation.

## Autocomplete

Suggests **existing** tags by prefix only — there is no curated list. In a new
database that means no suggestions, so typing an unseen tag shows a
"Press Enter to create #xyz" hint rather than dead-ending.

## Sport

`events.sport_id` still exists but is **nullable and icon-only**. The `sports`
table and its seed survive purely to supply `sportIcon` for cards, map pins and
the detail header. It is not a filter, not in search, and never sent by the create
form; `v_event_feed` therefore uses a `LEFT JOIN` (an inner join silently dropped
every sportless event from browse).

`events.skill_level` is likewise nullable now — the create form dropped its
selector, and every renderer hides the badge instead of showing an empty pill.
The `events_skill_level_check` constraint needed no change: a CHECK that evaluates
to NULL passes in Postgres.
