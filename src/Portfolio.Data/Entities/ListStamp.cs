namespace Portfolio.Data.Entities;

/// <summary>
/// The stamp of one ordered list (a content section), keyed by the section's key. Every write that can change the positions in the list (a move, a
/// new entry, a skill changing group) names the stamp it saw and replaces it in the same transaction, so two overlapping writes cannot both apply:
/// the second finds the stamp gone and is refused. A section with no row has not had its order changed yet; the first write creates the row.
/// New sections need no schema change: naming a new section key is enough.
/// </summary>
public class ListStamp
{
    /// <summary>Column length of <see cref="Section"/>.</summary>
    public const int SectionLength = 40;

    public required string Section { get; set; }

    /// <summary>The concurrency token: a 32 character GUID, or the placeholder a row starts with.</summary>
    public required string Stamp { get; set; }
}
