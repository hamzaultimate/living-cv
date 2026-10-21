using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class OwnerAccountServiceTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly TestDatabase _database = new();
    private readonly FakeClock _clock = new();
    private readonly OwnerPasswordHasher _hasher = new();

    public void Dispose() => _database.Dispose();

    private OwnerAccountService NewService() =>
        new(_database.CreateContext(), _hasher, _clock, NullLogger<OwnerAccountService>.Instance);

    private async Task<OwnerAccountService> WithOwnerAsync()
    {
        var service = NewService();
        Assert.Equal(CreateOwnerOutcome.Created, await service.CreateAsync("Owner", Password));
        return service;
    }

    [Fact]
    public async Task Create_SecondAttemptIsRefusedByTheDatabase_AndOnlyOneRowExists()
    {
        var first = NewService();
        var second = NewService();

        Assert.Equal(CreateOwnerOutcome.Created, await first.CreateAsync("owner", Password));
        Assert.Equal(CreateOwnerOutcome.AlreadyExists, await second.CreateAsync("someone-else", "another long password 1"));

        using var db = _database.CreateContext();
        var only = await db.OwnerAccounts.SingleAsync();
        Assert.Equal("owner", only.UserName);
    }

    [Fact]
    public async Task Create_StoresAHashNotThePassword()
    {
        await WithOwnerAsync();

        using var db = _database.CreateContext();
        var account = await db.OwnerAccounts.SingleAsync();
        Assert.DoesNotContain(Password, account.PasswordHash);
        Assert.NotEqual(Password, account.PasswordHash);
        Assert.False(string.IsNullOrEmpty(account.SecurityStamp));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("owner")]
    [InlineData("OWNER")]
    [InlineData("  owner  ")]
    [InlineData("\tOwner\n")]
    public async Task SignIn_AcceptsCaseAndWhitespaceVariantsOfTheName(string name)
    {
        var service = await WithOwnerAsync();

        var result = await service.SignInAsync(name, Password);

        Assert.Equal(SignInOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.Account);
    }

    [Fact]
    public async Task SignIn_FailsForWrongPasswordWrongNameOrNoAccount()
    {
        var empty = NewService();
        Assert.Equal(SignInOutcome.Failed, (await empty.SignInAsync("owner", Password)).Outcome);

        var service = await WithOwnerAsync();
        Assert.Equal(SignInOutcome.Failed, (await service.SignInAsync("owner", "not the password")).Outcome);
        Assert.Equal(SignInOutcome.Failed, (await service.SignInAsync("nobody", Password)).Outcome);
        Assert.Equal(SignInOutcome.Failed, (await service.SignInAsync(null, null)).Outcome);
    }

    [Fact]
    public async Task SignIn_LocksAfterFiveFailures_EvenForTheCorrectPassword_ThenRecovers()
    {
        var service = await WithOwnerAsync();

        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts; i++)
            Assert.Equal(SignInOutcome.Failed, (await service.SignInAsync("owner", "wrong password " + i)).Outcome);

        Assert.Equal(SignInOutcome.LockedOut, (await service.SignInAsync("owner", Password)).Outcome);

        _clock.Advance(OwnerAccountService.LockoutDuration - TimeSpan.FromSeconds(1));
        Assert.Equal(SignInOutcome.LockedOut, (await service.SignInAsync("owner", Password)).Outcome);

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password)).Outcome);
    }

    [Fact]
    public async Task Lockout_CannotBeDodgedByVaryingTheUserName()
    {
        var service = await WithOwnerAsync();
        string[] variants = ["OWNER", " owner", "Owner ", "nobody", "owner​", "ＯＷＮＥＲ"];

        foreach (var name in variants.Take(OwnerAccountService.MaxFailedAttempts))
            await service.SignInAsync(name, "wrong password");

        Assert.Equal(SignInOutcome.LockedOut, (await service.SignInAsync("owner", Password)).Outcome);
    }

    [Fact]
    public async Task SuccessfulSignIn_ResetsTheFailureCount()
    {
        var service = await WithOwnerAsync();
        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts - 1; i++)
            await service.SignInAsync("owner", "wrong password");

        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password)).Outcome);
        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts - 1; i++)
            await service.SignInAsync("owner", "wrong password");

        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password)).Outcome);
    }

    [Fact]
    public async Task ResetPassword_ChangesThePassword_ClearsLockout_AndEndsOldSessions()
    {
        var service = await WithOwnerAsync();
        var oldStamp = (await service.SignInAsync("owner", Password)).Account!.SecurityStamp;
        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts; i++)
            await service.SignInAsync("owner", "wrong password");

        Assert.True(await service.ResetPasswordAsync("a brand new long password"));

        Assert.False(await service.IsStampCurrentAsync(oldStamp));
        Assert.Equal(SignInOutcome.Failed, (await service.SignInAsync("owner", Password)).Outcome);
        var fresh = await service.SignInAsync("owner", "a brand new long password");
        Assert.Equal(SignInOutcome.Succeeded, fresh.Outcome);
        Assert.True(await service.IsStampCurrentAsync(fresh.Account!.SecurityStamp));
    }

    [Fact]
    public async Task ResetPassword_WithoutAnOwner_ReturnsFalse()
    {
        Assert.False(await NewService().ResetPasswordAsync("a brand new long password"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("short", false)]
    [InlineData("exactly12chr", true)]
    public void PasswordPolicy_EnforcesMinimumLength(string? password, bool ok) =>
        Assert.Equal(ok, PasswordPolicy.Validate(password) is null);

    [Fact]
    public void PasswordPolicy_EnforcesMaximumLength_AndRejectsTheUserName()
    {
        Assert.Null(PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength)));
        Assert.NotNull(PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength + 1)));
        Assert.NotNull(PasswordPolicy.Validate("owner-account-1", "Owner-Account-1"));
        Assert.NotNull(PasswordPolicy.Validate(new string(' ', 14)));
    }
}
