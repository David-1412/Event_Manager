namespace SportMeet.Domain.Entities;

/// <summary>
/// A forum section: Q&amp;A, Marketplace &amp; Jobs, General Chat, Study Groups.
///
/// "All" is deliberately not a row. It is the absence of a category filter, and as a
/// row it could be chosen when posting - a post filed under "All" would then be the
/// only one that appears in exactly one listing instead of every listing.
///
/// Seeded by the migration rather than created through the API: the set is part of
/// the product's shape, and the slugs are what the frontend's URLs carry.
/// </summary>
public class ForumCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>Display order of the chips; slugs sort badly ("general" before "qa").</summary>
    public int SortOrder { get; set; }
}
