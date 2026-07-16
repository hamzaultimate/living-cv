namespace Portfolio.Data.Entities;

/// <summary>An external link (label + url), stored as JSON on its owner.</summary>
public class Link
{
    public required string Label { get; set; }
    public required string Url { get; set; }
}

/// <summary>A project screenshot for the gallery/lightbox (image url + optional caption). Stored as JSON.</summary>
public class ProjectImage
{
    public required string Url { get; set; }
    public string? Caption { get; set; }
}

public enum ProofKind { Link, Image, Pdf }

/// <summary>Evidence for a project/certification — a link, image, or PDF. Stored as JSON.</summary>
public class Proof
{
    public required string Label { get; set; }
    public required string Url { get; set; }
    public ProofKind Kind { get; set; } = ProofKind.Link;
}

public enum CertificationStatus { Earned, InProgress, Planned }

public enum TalkType { Video, Talk }
