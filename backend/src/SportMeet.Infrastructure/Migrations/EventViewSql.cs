namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// The single definition of sportsmeet.v_event_feed, shared by Init_Views (which
/// creates it on a fresh database) and Add_Tags (which replaces it on an existing
/// one).
///
/// EF cannot snapshot a view, so editing the SQL anywhere produces no migration
/// diff - the failure shows up as a missing column at query time instead. Making
/// both migrations read one constant removes the version of that mistake where the
/// two definitions quietly disagree and a developer's fresh database behaves
/// differently from a colleague's upgraded one.
///
/// Both call sites use CREATE OR REPLACE, but their ORDER is not irrelevant:
/// PreTagsDefinition must run first, because a view's SELECT is parsed when the
/// view is created and cannot reference tags/event_tags before Add_Tags makes them.
/// A fresh database therefore lands on PreTagsDefinition and is immediately
/// replaced by Add_Tags; an upgraded database goes straight to Definition. Either
/// path ends identically, which is what keeping both strings here guarantees.
/// </summary>
internal static class EventViewSql
{
    /// <summary>The current shape: LEFT JOIN on sports (sport_id became nullable)
    /// plus the comma-joined tag aggregate.
    ///
    /// Frozen at the Add_Event_Visibility boundary. Migrations are replayed in
    /// order on a fresh database, and Add_Tags and Increase_Event_Title_Length both
    /// execute this string, so adding a column here makes those earlier migrations
    /// reference a column that does not exist yet - 42703 "column e.visibility does
    /// not exist" on every fresh database, while an already-upgraded one never
    /// replays them and so appears fine. A new view column belongs in its own
    /// migration's private constant instead, the way Init_Views keeps its original
    /// definition rather than reading this one.</summary>
    public const string Definition = """
        CREATE OR REPLACE VIEW sportsmeet.v_event_feed AS
        SELECT
            e.id,
            e.host_id,
            e.title,
            e.description,
            e.sport_id,
            e.venue_name,
            e.address,
            e.place_id,
            e.lat,
            e.lng,
            e.timezone,
            e.start_at,
            e.end_at,
            e.max_participants,
            e.skill_level,
            e.cost,
            e.status,
            e.cancelled_at,
            e.created_at,
            e.updated_at,
            -- Correlated rather than a joined GROUP BY: the planner turns it into a
            -- per-row index scan on the composite event_participants key, which is
            -- cheaper than aggregating the whole table for a 20-row page.
            (SELECT COUNT(*)::int FROM sportsmeet.event_participants p WHERE p.event_id = e.id)
                AS current_participants,
            s.name AS sport_name,
            s.slug AS sport_slug,
            s.icon AS sport_icon,
            u.name AS host_name,
            u.photo_url AS host_photo_url,
            -- MUST stay the final column: CREATE OR REPLACE VIEW only allows new
            -- columns to be APPENDED, so inserting this earlier makes Postgres
            -- positionally match it against sport_name and fail 42P16 "cannot change
            -- name of view column" (measured on a real upgrade, not assumed).
            -- Aggregated in a scalar subquery rather than a LEFT JOIN + string_agg:
            -- joining event_tags multiplies the event row, which would inflate both
            -- the participant COUNT above and the page's LIMIT. Ordered by name so
            -- the same tag set serialises identically across refetches and the
            -- client does not reorder its chips.
            (SELECT string_agg(t.name::text, ',' ORDER BY t.name)
               FROM sportsmeet.event_tags et
               JOIN sportsmeet.tags t ON t.id = et.tag_id
              WHERE et.event_id = e.id) AS tags
        FROM sportsmeet.events e
        -- LEFT, not JOIN: sport_id became nullable when tags replaced the sport
        -- vocabulary. With the inner join an event without a sport vanished from
        -- browse entirely - silently, and with no error to trace.
        LEFT JOIN sportsmeet.sports s ON s.id = e.sport_id
        JOIN sportsmeet.users u ON u.id = e.host_id;
        """;

    /// <summary>The v_event_feed shape as of Add_Event_Visibility: Definition plus
    /// e.visibility appended last. Used only by Add_Event_Interests_Counts.Down()
    /// to restore the pre-interest view. A private constant there would duplicate
    /// the whole SELECT; keeping it here groups every view shape in one file, which
    /// is what this class exists for. It references events.visibility, so it can
    /// only run after Add_Event_Visibility has created that column.</summary>
    public const string AddEventVisibilityView = """
        CREATE OR REPLACE VIEW sportsmeet.v_event_feed AS
        SELECT
            e.id,
            e.host_id,
            e.title,
            e.description,
            e.sport_id,
            e.venue_name,
            e.address,
            e.place_id,
            e.lat,
            e.lng,
            e.timezone,
            e.start_at,
            e.end_at,
            e.max_participants,
            e.skill_level,
            e.cost,
            e.status,
            e.cancelled_at,
            e.created_at,
            e.updated_at,
            (SELECT COUNT(*)::int FROM sportsmeet.event_participants p WHERE p.event_id = e.id)
                AS current_participants,
            s.name AS sport_name,
            s.slug AS sport_slug,
            s.icon AS sport_icon,
            u.name AS host_name,
            u.photo_url AS host_photo_url,
            (SELECT string_agg(t.name::text, ',' ORDER BY t.name)
               FROM sportsmeet.event_tags et
               JOIN sportsmeet.tags t ON t.id = et.tag_id
              WHERE et.event_id = e.id) AS tags,
            e.visibility
        FROM sportsmeet.events e
        LEFT JOIN sportsmeet.sports s ON s.id = e.sport_id
        JOIN sportsmeet.users u ON u.id = e.host_id;
        """;

    /// <summary>The shape Add_Tags replaces, used only by its Down(): inner join on
    /// sports, no tag aggregate. Kept here so a rollback does not have to hand-write
    /// SQL inside a migration body.</summary>
    public const string PreTagsDefinition = """
        CREATE OR REPLACE VIEW sportsmeet.v_event_feed AS
        SELECT
            e.id,
            e.host_id,
            e.title,
            e.description,
            e.sport_id,
            e.venue_name,
            e.address,
            e.place_id,
            e.lat,
            e.lng,
            e.timezone,
            e.start_at,
            e.end_at,
            e.max_participants,
            e.skill_level,
            e.cost,
            e.status,
            e.cancelled_at,
            e.created_at,
            e.updated_at,
            (SELECT COUNT(*)::int FROM sportsmeet.event_participants p WHERE p.event_id = e.id)
                AS current_participants,
            s.name AS sport_name,
            s.slug AS sport_slug,
            s.icon AS sport_icon,
            u.name AS host_name,
            u.photo_url AS host_photo_url
        FROM sportsmeet.events e
        JOIN sportsmeet.sports s ON s.id = e.sport_id
        JOIN sportsmeet.users u ON u.id = e.host_id;
        """;
}
