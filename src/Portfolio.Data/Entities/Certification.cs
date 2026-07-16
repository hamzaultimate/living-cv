namespace Portfolio.Data.Entities;

/// <summary>A certification — earned, in-progress, or planned — with optional proof.</summary>
public class Certification
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Issuer { get; set; }
    public CertificationStatus Status { get; set; } = CertificationStatus.Earned;
    public string? Date { get; set; }
    public string? CredentialUrl { get; set; }
    public string? Category { get; set; }

    /// <summary>URL to the credential badge image (e.g. a Credly/Microsoft badge) shown on the certs page.</summary>
    public string? BadgeImageUrl { get; set; }

    /// <summary>URL to the actual certificate file (PDF or image) — powers the "View certificate" action.</summary>
    public string? CertificateUrl { get; set; }
    public List<Proof> Proofs { get; set; } = new();
    public int SortOrder { get; set; }
}
