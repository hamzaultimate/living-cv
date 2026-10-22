namespace Portfolio.Data.Entities;

/// <summary>
/// A content row the admin can reorder and edit safely from two tabs: <see cref="SortOrder"/> is its position and
/// <see cref="ConcurrencyStamp"/> changes on every admin write, so a write made from a stale copy is refused instead of overwriting.
/// Rows written before the admin existed have no stamp.
/// </summary>
public interface IOrderedContent : IStampedContent
{
    /// <summary>Column length of <see cref="IStampedContent.ConcurrencyStamp"/> (a 32 character GUID).</summary>
    public const int StampLength = IStampedContent.StampLength;

    int SortOrder { get; set; }
}

/// <summary>A content row the admin edits and removes safely from two tabs, whose position is not stored (blog posts are ordered by their date).</summary>
public interface IStampedContent
{
    /// <summary>Column length of <see cref="ConcurrencyStamp"/> (a 32 character GUID).</summary>
    public const int StampLength = 32;

    int Id { get; }
    string? ConcurrencyStamp { get; set; }
}
