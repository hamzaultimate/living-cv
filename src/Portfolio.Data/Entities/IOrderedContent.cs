namespace Portfolio.Data.Entities;

/// <summary>
/// A content row the admin can reorder and edit safely from two tabs: <see cref="SortOrder"/> is its position and
/// <see cref="ConcurrencyStamp"/> changes on every admin write, so a write made from a stale copy is refused instead of overwriting.
/// Rows written before the admin existed have no stamp.
/// </summary>
public interface IOrderedContent
{
    /// <summary>Column length of <see cref="ConcurrencyStamp"/> (a 32 character GUID).</summary>
    public const int StampLength = 32;

    int Id { get; }
    int SortOrder { get; set; }
    string? ConcurrencyStamp { get; set; }
}
