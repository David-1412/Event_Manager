-- Sample upcoming events for LOCAL development - never run this against production:
-- these are made-up events, and real students would see them as real.
--
--   docker exec -i sportmeet-postgres psql -U sportmeet -d sportmeet < scripts/sample-events.sql
--
-- Re-runnable: it replaces its own events (fixed ids 22222222-2222-4222-8222-...) and
-- dates each one on the next occurrence of its weekday in Melbourne (dow: 0 = Sunday,
-- week: 0 = within the next 7 days, 1 = the week after), so a title like "Futsal
-- Friday" is always on a Friday, and running it again after they pass moves them on. Hosts are the seeded Demo Host (...0001) and Casey
-- Reviewer (...0003), never the default demo viewer Sam Rivera (...0002), so the
-- local demo identity can join every one of them.

BEGIN;

DELETE FROM sportsmeet.events WHERE id::text LIKE '22222222-2222-4222-8222-%';

INSERT INTO sportsmeet.events (
    id, host_id, title, description, sport_id, venue_name, address, lat, lng, timezone,
    start_at, end_at, max_participants, skill_level, cost, status, created_at, updated_at,
    visibility, requires_join_approval)
SELECT
    v.id::uuid, v.host::uuid, v.title, v.descr, v.sport, v.venue, v.addr, v.lat, v.lng, 'Australia/Melbourne',
    (t.today + (((v.dow - extract(dow FROM t.today)::int - 1 + 7) % 7) + 1 + 7 * v.week) * interval '1 day' + v.start_t)
        AT TIME ZONE 'Australia/Melbourne',
    (t.today + (((v.dow - extract(dow FROM t.today)::int - 1 + 7) % 7) + 1 + 7 * v.week) * interval '1 day' + v.end_t)
        AT TIME ZONE 'Australia/Melbourne',
    v.maxp, v.skill, v.cost, 'Scheduled', now(), now(), 'Public', v.approval
FROM (VALUES
    ('22222222-2222-4222-8222-000000000001', '00000000-0000-0000-0000-000000000001',
     'Monash Badminton Social',
     'Four courts, rotating doubles every game so you play with everyone. Shuttles provided, bring a racket (a few spares at the desk).',
     1, 'Monash Sport, Clayton', 'Sports Walk, Clayton VIC 3800', -37.9112, 145.1351,
     4, 0, time '18:00', time '20:00', 16, 'Intermediate', 5.00::numeric, false),

    ('22222222-2222-4222-8222-000000000002', '00000000-0000-0000-0000-000000000003',
     'Sunrise 5k around Clayton campus',
     'Easy conversational pace, one loop of the ring road. Nobody gets left behind - the back of the pack sets the pace. Coffee after at the Campus Centre.',
     3, 'Campus Centre lawn', '21 Chancellors Walk, Clayton VIC 3800', -37.9115, 145.1330,
     5, 0, time '06:45', time '07:30', NULL, 'Beginner', NULL, false),

    ('22222222-2222-4222-8222-000000000003', '00000000-0000-0000-0000-000000000001',
     'Thursday Night 3v3 Basketball',
     'Half-court 3v3, winners stay on. Indoor court, non-marking shoes please.',
     2, 'Monash Sport Indoor Courts', 'Sports Walk, Clayton VIC 3800', -37.9108, 145.1358,
     4, 0, time '19:00', time '21:00', 12, 'Advanced', 4.00::numeric, false),

    ('22222222-2222-4222-8222-000000000004', '00000000-0000-0000-0000-000000000003',
     'Futsal Friday',
     'Five-a-side futsal, mixed teams picked on the night. Bibs and balls provided.',
     4, 'Monash Sport Futsal Court', 'Sports Walk, Clayton VIC 3800', -37.9104, 145.1362,
     5, 0, time '17:30', time '19:00', 14, 'Intermediate', 6.00::numeric, false),

    ('22222222-2222-4222-8222-000000000005', '00000000-0000-0000-0000-000000000001',
     'Beginners Tennis Clinic',
     'Coached session for anyone who has never played or is getting back into it. Rackets and balls provided. Limited spots, so the coach approves each request.',
     5, 'Monash Tennis Courts', 'Ferntree Gully Rd, Clayton VIC 3800', -37.9086, 145.1381,
     6, 0, time '10:00', time '11:30', 8, 'Beginner', 10.00::numeric, true),

    ('22222222-2222-4222-8222-000000000006', '00000000-0000-0000-0000-000000000003',
     'Sunday Social Cricket',
     'Tape-ball cricket, eight overs a side. All levels, we will sort teams so it stays close.',
     6, 'Jock Marshall Oval', 'Wellington Rd, Clayton VIC 3800', -37.9145, 145.1309,
     0, 0, time '13:00', time '16:00', 22, 'Intermediate', NULL, false),

    ('22222222-2222-4222-8222-000000000007', '00000000-0000-0000-0000-000000000001',
     'Mixed Netball Training',
     'Drills then a practice match. Great if you are thinking of joining a uni league team next season.',
     7, 'Monash Sport Netball Courts', 'Sports Walk, Clayton VIC 3800', -37.9118, 145.1366,
     2, 0, time '18:30', time '20:00', 20, 'Intermediate', 3.00::numeric, false),

    ('22222222-2222-4222-8222-000000000008', '00000000-0000-0000-0000-000000000003',
     'Beach Volleyball at St Kilda',
     'Two nets booked on the sand. Bring water and sunscreen; we head to dinner on Acland St after.',
     8, 'St Kilda Beach Volleyball Courts', 'Jacka Blvd, St Kilda VIC 3182', -37.8655, 144.9737,
     6, 1, time '16:00', time '18:30', 16, 'Beginner', 8.00::numeric, false),

    ('22222222-2222-4222-8222-000000000009', '00000000-0000-0000-0000-000000000001',
     'Albert Park Lake Loop',
     'Five km around the lake at a steady pace, then stretching on the grass. Meet at the boathouse.',
     3, 'Albert Park Lake Boathouse', 'Lakeside Dr, Albert Park VIC 3206', -37.8466, 144.9709,
     0, 1, time '07:00', time '08:00', NULL, 'Intermediate', NULL, false),

    ('22222222-2222-4222-8222-000000000010', '00000000-0000-0000-0000-000000000003',
     'End of Semester Basketball Tournament',
     'Teams of five, round robin then finals. Sign up solo and get placed on a team. Prizes for the winners.',
     2, 'Melbourne Sports and Aquatic Centre', 'Aughtie Dr, Albert Park VIC 3206', -37.8429, 144.9608,
     3, 1, time '12:00', time '17:00', 40, 'Advanced', 15.00::numeric, false)
) AS v(id, host, title, descr, sport, venue, addr, lat, lng, dow, week, start_t, end_t, maxp, skill, cost, approval)
CROSS JOIN (SELECT date_trunc('day', now() AT TIME ZONE 'Australia/Melbourne') AS today) AS t;

COMMIT;

SELECT title, to_char(start_at AT TIME ZONE 'Australia/Melbourne', 'Dy DD Mon HH24:MI') AS starts
FROM sportsmeet.events
WHERE id::text LIKE '22222222-2222-4222-8222-%'
ORDER BY start_at;
