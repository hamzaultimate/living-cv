using Portfolio.Data.Entities;

namespace Portfolio.Web.Services;

/// <summary>Badge styling and wording for a certification's status, shared by every page that shows one.</summary>
public static class CertificationBadges
{
    public static string Css(CertificationStatus status) => status switch
    {
        CertificationStatus.Earned => "badge badge-earned",
        CertificationStatus.InProgress => "badge badge-progress",
        _ => "badge badge-planned",
    };

    public static string Label(CertificationStatus status, CertificationsCopy copy) => status switch
    {
        CertificationStatus.Earned => copy.StatusEarned,
        CertificationStatus.InProgress => copy.StatusInProgress,
        _ => copy.StatusPlanned,
    };
}
