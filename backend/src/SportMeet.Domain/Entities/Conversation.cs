namespace SportMeet.Domain.Entities;

/// <summary>
/// A direct-message thread between exactly two people.
///
/// One conversation per pair, stored with the lower user id in <see cref="UserAId"/>
/// (a CHECK enforces <c>user_a_id &lt; user_b_id</c>) and a unique key on the pair.
/// Ordering the pair is what makes "Alice messages Bob" and "Bob messages Alice"
/// land in the same row instead of two, and it lets the database - not a
/// check-then-insert in code - guarantee there is never a duplicate thread.
///
/// Read state is a timestamp per side rather than a flag per message: "unread" is
/// every message from the other person after my <c>LastReadAt</c>, so opening a
/// thread is one UPDATE however many messages it holds.
///
/// The last-message columns are denormalised for the inbox list, which would
/// otherwise need the newest message of every conversation on every load.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; }

    public Guid UserAId { get; set; }
    public User UserA { get; set; } = null!;

    public Guid UserBId { get; set; }
    public User UserB { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public Guid? LastSenderId { get; set; }

    public DateTimeOffset? UserALastReadAt { get; set; }
    public DateTimeOffset? UserBLastReadAt { get; set; }

    /// <summary>
    /// The pair in canonical order: lower id first, by Postgres's uuid ordering, which
    /// the CHECK <c>user_a_id &lt; user_b_id</c> enforces.
    ///
    /// Postgres compares the 16 bytes in written order, which is the order of the
    /// canonical lower-case text, so the text is what is compared here - stated
    /// directly rather than relying on how a Guid happens to compare in .NET. (The
    /// trap this avoids is <see cref="Guid.ToByteArray()"/>, whose first three groups
    /// are little-endian and sort differently.) MessagingSqlTests checks the two
    /// orderings agree against a real database.
    /// </summary>
    public static (Guid A, Guid B) Order(Guid one, Guid two) =>
        string.CompareOrdinal(one.ToString("D"), two.ToString("D")) < 0 ? (one, two) : (two, one);

    public bool Includes(Guid userId) => UserAId == userId || UserBId == userId;

    public Guid OtherThan(Guid userId) => UserAId == userId ? UserBId : UserAId;
}

/// <summary>One message in a <see cref="Conversation"/>. Plain text only.</summary>
public class DirectMessage
{
    public Guid Id { get; set; }

    /// <summary>Insertion order, the keyset for paging back through history.
    /// Database-generated.</summary>
    public long Seq { get; set; }

    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public Guid SenderUserId { get; set; }
    public User Sender { get; set; } = null!;

    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
