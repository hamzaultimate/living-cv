using Portfolio.Web.Configuration;
using Xunit;

namespace Portfolio.Web.Tests.Configuration;

public class SiteOptionsInitialsTests
{
    [Theory]
    [InlineData("alex rivera", "AR")]
    [InlineData("Alex", "A")]
    [InlineData("  Alex   Q.   Rivera ", "AR")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData("\U00010428one \U00010428two", "\U00010400\U00010400")]   // Deseret letters live outside the BMP
    [InlineData("\U0001F600 smile", "\U0001F600S")]
    [InlineData("é́mile Zola", "É́Z")]                   // a base letter plus a combining mark stays together
    public void Initials_TakeWholeCharacters_NotHalfOfASurrogatePair(string name, string expected)
    {
        var initials = new SiteOptions { OwnerName = name }.Initials;

        Assert.Equal(expected.Normalize(), initials.Normalize());
        Assert.DoesNotContain('\uFFFD', initials);
        Assert.DoesNotContain(initials.EnumerateRunes(), r => r.Value == 0xFFFD);
    }
}
