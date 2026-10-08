# Event import (paste to event)

Paste an invitation, message or caption on `/create` and the form fills itself. The goal is
**a person can paste event text and publish in under 30 seconds**. The number the feature
is judged by is the **median time from opening Create Event to publishing**, not extraction
accuracy: a slightly wrong fill the user fixes in two seconds beats a perfect one that
takes a click to start.

## How it works

```
paste ─▶ POST /api/imports/text ─▶ extract ─▶ geocode ─▶ flag ─▶ record ─▶ form fills
publish ─▶ POST /api/publish-metrics ─▶ record time, and diff proposal vs published event
```

- Extraction is the existing `IEventExtractor` (the AI extractor, falling back to the basic
  one). `ExtractedEvent` is unchanged.
- After extraction nothing knows the input was text. A new input (a poster, a screenshot) is
  one new `case` in `ImportService.ExtractAsync`; geocoding, flagging, recording and the
  response are shared.
- The import is **recorded but is not a draft**. The form is filled directly and publishing
  uses the normal create path, so there is no "approve" step between paste and publish.
- Fields the user should check get a marker (`FieldFlagger`): missing, assumed (a default is
  showing), unclear (a relative date, no time, low-confidence title), past, or an
  unconfirmed pin. A marker clears when the field is edited or the user taps "Looks right".
  Markers never block Publish.
- Pasting a **lone link** is refused with a message, not fetched. The refusal is recorded
  (host and path, no query string) as a demand signal for link import.

## Configuration

| Setting | Env var | Default | Notes |
|---|---|---|---|
| `Google:GeocodingApiKey` | `GOOGLE_GEOCODING_API_KEY` | empty | A server key restricted to the Geocoding API. **Not** the browser Maps key. Empty means imports still work and the pin is left for the user. |
| `OpenAi:ApiKey` | `OPENAI_API_KEY` | empty | Empty means the basic extractor answers: it finds a title and sometimes a date, never a venue. The UI says so. |
| `Import:MaxPerUserPerHour` | | 30 | Per-user rate limit (429 beyond it). |
| `Import:MaxPerDay` | | 2000 | Global ceiling in any rolling 24 h (503 beyond it). |
| `Import:SourceTextRetentionDays` | | 30 | Pasted text is blanked on the next import after this. Structured columns stay. |

Every import route needs a verified sign-in (extraction spends money).

## Measuring it

Run in `psql` (`docker exec -it sportmeet-postgres psql -U sportmeet -d sportmeet`).

### 1. The success metric: median time to publish, by route

`manual` is the baseline, `import` is the feature, `draft` is a saved draft being finished.

```sql
SELECT path, count(*) AS events,
       round((percentile_cont(0.5) WITHIN GROUP (ORDER BY duration_ms) / 1000.0)::numeric, 1) AS median_s,
       round((percentile_cont(0.9) WITHIN GROUP (ORDER BY duration_ms) / 1000.0)::numeric, 1) AS p90_s,
       round(100.0 * count(*) FILTER (WHERE duration_ms < 30000) / count(*), 0) AS pct_under_30s
FROM sportsmeet.event_publish_metrics
WHERE created_at > now() - interval '30 days'
GROUP BY path ORDER BY path;
```

The duration runs from the Create page mounting to the publish succeeding, so it includes
time spent thinking or distracted. Compare medians, not means.

### 2. Real-world extraction accuracy, per field

`kept` = the user left the extractor's value alone. `changed` / `cleared` = it was wrong or
unwanted. `filled` = the extractor found nothing and the published event has a value (this
includes an accepted form default). `pct_right` counts only fields the extractor attempted.

```sql
SELECT f.key AS field,
       count(*) FILTER (WHERE f.value = 'kept') AS kept,
       count(*) FILTER (WHERE f.value = 'changed') AS changed,
       count(*) FILTER (WHERE f.value = 'cleared') AS cleared,
       count(*) FILTER (WHERE f.value = 'filled') AS filled,
       round(100.0 * count(*) FILTER (WHERE f.value = 'kept')
             / nullif(count(*) FILTER (WHERE f.value IN ('kept', 'changed', 'cleared')), 0), 0) AS pct_right
FROM sportsmeet.event_imports i, jsonb_each_text(i.field_outcomes) f
WHERE i.field_outcomes IS NOT NULL
GROUP BY f.key ORDER BY f.key;
```

A nudged pin within 100 m counts as `kept` (the map picker re-geocodes on pick).

### 3. Funnel

```sql
SELECT status, count(*) AS imports, count(published_event_id) AS published,
       round(avg(latency_ms)) AS avg_ms,
       count(*) FILTER (WHERE model LIKE 'heuristic%') AS basic
FROM sportsmeet.event_imports
WHERE created_at > now() - interval '30 days'
GROUP BY status;
```

`basic` should be zero in production. If it is not, the AI extractor is unconfigured or failing.

### 4. Are the "check this" markers earning their place?

A good marker is on fields people then edit. If flagged fields are edited about as often as
unflagged ones, the rule is noise and should be removed.

```sql
WITH flagged AS (
  SELECT i.id, CASE fl->>'field' WHEN 'venue' THEN 'location' ELSE fl->>'field' END AS field
  FROM sportsmeet.event_imports i, jsonb_array_elements(i.flags) fl
  WHERE i.field_outcomes IS NOT NULL),
outcomes AS (
  SELECT i.id, o.key AS field, o.value AS outcome
  FROM sportsmeet.event_imports i, jsonb_each_text(i.field_outcomes) o)
SELECT o.field, (f.id IS NOT NULL) AS was_flagged, count(*) AS n,
       round(100.0 * count(*) FILTER (WHERE o.outcome IN ('changed', 'cleared', 'filled')) / count(*), 0) AS pct_edited
FROM outcomes o LEFT JOIN flagged f ON f.id = o.id AND f.field = o.field
WHERE o.field IN ('title', 'startAt', 'endAt', 'maxParticipants', 'location')
GROUP BY o.field, was_flagged ORDER BY o.field, was_flagged;
```

### 5. Demand for link import, by site

```sql
SELECT split_part(split_part(source_text, '://', 2), '/', 1) AS host, count(*) AS attempts
FROM sportsmeet.event_imports
WHERE status = 'LinkRefused'
GROUP BY 1 ORDER BY 2 DESC;
```

Pasted text is blanked after the retention window, so run this at least monthly.

## Roadmap decision: poster upload (A) or link import (B) next?

**Recommendation: A, poster and screenshot upload. Revisit B only on the conditions below.**

| | **A. Poster / screenshot** | **B. Link import** |
|---|---|---|
| **Expected user value** | High and broad, but unmeasured. Community events circulate as images in chats and social posts, and a photo is one tap on a phone. | Narrow. By design it works only for approved domains, and today **none are approved**, so it serves nobody until a permission exists. |
| **What limits the value** | Model accuracy on stylised text (unmeasured; needs about 30 real posters to test). | Legal approvals, which engineering cannot speed up. |
| **Complexity** (estimate) | About 8-10 days. The upload endpoint, file-type check and storage exist. Needs client downscaling and a vision request on the existing prompt and schema. | About 8-10 days. A hardened fetcher (SSRF, redirects, size and time limits), a domain-policy table and admin, structured-data and text extraction. |
| **Legal risk** | Low. The content is user-provided. Residual: images go to the model provider (needs a privacy notice and a deletion policy), screenshots can show other people's names, and reusing a poster as the event image needs the user to attest they may. | High for the big platforms (see the terms review). Low for a domain with written permission, but each needs evidence recorded. We also become the party making the requests. |
| **Security risk** | Low. | Real: SSRF with Postgres on the same network, redirect handling, payload and timeout attacks. |
| **Maintenance** | Prompt tuning and model cost. No site-specific breakage. | Per-domain breakage when markup changes, policy upkeep, robots and terms drift. |

**Conditions to revisit B** (any one):
1. At least two domains have written permission, or are sites we operate.
2. Query 5 shows sustained demand concentrated on a few approvable hosts.

**Conditions to revisit A's priority:** if query 1 shows paste imports already publish in
well under 30 seconds and few events are being created from images, the poster path has less
to give than assumed.

## Not built, on purpose

Posters and screenshots, link fetching, any platform connector, Facebook or Instagram
ingestion, timezone resolution from the pin (this is a Melbourne product and the form edits
in Melbourne time), a draft record for imports.

## Known limitations

- The AI extractor's real accuracy is **unmeasured**: no OpenAI key was available. Query 2 is
  how it gets measured in production. The thresholds proposed earlier (title 95%, start time
  85%, venue 70%, address 60%, fabricated values 2%) are untested targets.
- Without an AI key the basic extractor takes the first line as the title (flagged as
  unclear), finds a date sometimes, and never finds a venue.
- Google's rules on storing geocoding results have not been verified. Coordinates are stored
  on the import and on the published event.
- The shared 401 message says "Sign in to view or manage your drafts", whatever the route.
  The import panel replaces it with its own wording.
