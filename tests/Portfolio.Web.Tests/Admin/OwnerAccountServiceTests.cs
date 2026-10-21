using Microsoft.AspNetCore.DataProtection;
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
    private readonly SignInGate _gate = new();
    private readonly SignInThrottle _throttle;
    private readonly KnownDevices _devices;

    public OwnerAccountServiceTests()
    {
        _throttle = new SignInThrottle(_clock);
        _devices = new KnownDevices(DataProtectionProvider.Create("owner-account-service-tests"), _clock, "lcv-device", alwaysSecure: true);
    }

    public void Dispose() => _database.Dispose();

    private OwnerAccountService NewService() =>
        new(_database.CreateContext(), _hasher, _gate, _throttle, _devices, _clock, NullLogger<OwnerAccountService>.Instance);

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

    private async Task<string> TrustedDeviceTokenAsync(OwnerAccountService service) =>
        _devices.Issue((await service.SignInAsync("owner", Password)).Account!);

    private static async Task LockAsync(OwnerAccountService service)
    {
        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts; i++)
            await service.SignInAsync("owner", "wrong password");
    }

    [Fact]
    public async Task KnownDevice_StillSignsInWhileTheAccountIsLockedOut_ButNeedsThePassword()
    {
        var service = await WithOwnerAsync();
        var token = await TrustedDeviceTokenAsync(service);
        await LockAsync(service);
        Assert.Equal(SignInOutcome.LockedOut, (await service.SignInAsync("owner", Password)).Outcome);

        var device = new SignInContext(DeviceToken: token);
        Assert.Equal(SignInOutcome.Failed, (await service.SignInAsync("owner", "wrong password", device)).Outcome);
        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password, device)).Outcome);

        // Proving the password from a known device also ends the lockout for everyone.
        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password)).Outcome);
    }

    [Fact]
    public async Task KnownDevice_FailuresDoNotCountTowardsTheGlobalLockout()
    {
        var service = await WithOwnerAsync();
        var device = new SignInContext(DeviceToken: await TrustedDeviceTokenAsync(service));

        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts * 2; i++)
            await service.SignInAsync("owner", "wrong password", device);

        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password)).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task InvalidDeviceTokens_DoNotBypassTheLockout(string? token)
    {
        var service = await WithOwnerAsync();
        await LockAsync(service);

        Assert.Equal(SignInOutcome.LockedOut, (await service.SignInAsync("owner", Password, new SignInContext(DeviceToken: token))).Outcome);
    }

    [Fact]
    public async Task ADeviceTokenFromAnotherInstallation_IsNotAccepted()
    {
        var service = await WithOwnerAsync();
        var account = (await service.SignInAsync("owner", Password)).Account!;
        var foreign = new KnownDevices(DataProtectionProvider.Create("some-other-app"), _clock, "lcv-device", alwaysSecure: true);
        await LockAsync(service);

        Assert.Equal(SignInOutcome.LockedOut, (await service.SignInAsync("owner", Password, new SignInContext(DeviceToken: foreign.Issue(account)))).Outcome);
    }

    [Fact]
    public async Task ResetPassword_InvalidatesKnownDevices()
    {
        var service = await WithOwnerAsync();
        var token = await TrustedDeviceTokenAsync(service);
        Assert.True(await service.ResetPasswordAsync("a brand new long password"));
        await LockAsync(service);

        var result = await service.SignInAsync("owner", "a brand new long password", new SignInContext(DeviceToken: token));

        Assert.Equal(SignInOutcome.LockedOut, result.Outcome);
    }

    [Fact]
    public async Task KnownDevice_ExpiresAfterItsLifetime()
    {
        var service = await WithOwnerAsync();
        var account = (await service.SignInAsync("owner", Password)).Account!;
        var token = _devices.Issue(account);
        Assert.True(_devices.IsValid(token, account));

        _clock.Advance(KnownDevices.Lifetime - TimeSpan.FromDays(1));
        Assert.True(_devices.IsValid(token, account));
        _clock.Advance(TimeSpan.FromDays(2));
        Assert.False(_devices.IsValid(token, account));
    }

    [Fact]
    public async Task Throttle_TurnsAwayAFloodFromOneAddress_BeforeTheAccountCounterOrTheHash()
    {
        var service = await WithOwnerAsync();
        var attacker = new SignInContext(ClientKey: "198.51.100.7");

        for (var i = 0; i < SignInThrottle.MaxAttempts; i++)
            await service.SignInAsync("owner", "wrong password", attacker);

        // Over the allowance: refused without any password check, even for the right password.
        Assert.Equal(SignInOutcome.Throttled, (await service.SignInAsync("owner", Password, attacker)).Outcome);

        // The address used its own allowance; the account counter only saw the first few (it locked at five).
        // A different address is not affected by the flood and, once the lockout ends, signs in.
        _clock.Advance(OwnerAccountService.LockoutDuration + TimeSpan.FromSeconds(1));
        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password, new SignInContext(ClientKey: "203.0.113.9"))).Outcome);
    }

    [Fact]
    public async Task Throttle_RejectedAttemptsDoNotTouchTheAccountCounter()
    {
        var service = await WithOwnerAsync();
        var attacker = new SignInContext(ClientKey: "198.51.100.7");
        // Use up the allowance with attempts that do not reach the lockout threshold: succeed in between.
        for (var i = 0; i < SignInThrottle.MaxAttempts; i++)
            _throttle.TryAcquire(attacker.ClientKey!);

        for (var i = 0; i < 50; i++)
            Assert.Equal(SignInOutcome.Throttled, (await service.SignInAsync("owner", "wrong password", attacker)).Outcome);

        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password)).Outcome);
    }

    [Fact]
    public async Task Throttle_AllowsAgainAfterTheWindow_AndSuccessClearsIt()
    {
        var service = await WithOwnerAsync();
        var client = new SignInContext(ClientKey: "198.51.100.7");
        for (var i = 0; i < SignInThrottle.MaxAttempts - 1; i++)
            _throttle.TryAcquire(client.ClientKey!);

        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password, client)).Outcome);
        for (var i = 0; i < SignInThrottle.MaxAttempts; i++)
            Assert.True(_throttle.TryAcquire(client.ClientKey!));
        Assert.False(_throttle.TryAcquire(client.ClientKey!));

        _clock.Advance(SignInThrottle.Window + TimeSpan.FromSeconds(1));
        Assert.True(_throttle.TryAcquire(client.ClientKey!));
    }

    [Fact]
    public void Throttle_StaysBounded_AndGroupsIpv6ByPrefix()
    {
        for (var i = 0; i < SignInThrottle.MaxTracked + 500; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(_throttle.TryAcquire("client-" + i));
        }

        Assert.Equal(SignInThrottle.KeyFor(System.Net.IPAddress.Parse("2001:db8:1:2::1")), SignInThrottle.KeyFor(System.Net.IPAddress.Parse("2001:db8:1:2:ffff::9")));
        Assert.NotEqual(SignInThrottle.KeyFor(System.Net.IPAddress.Parse("2001:db8:1:2::1")), SignInThrottle.KeyFor(System.Net.IPAddress.Parse("2001:db8:1:3::1")));
        Assert.Equal("192.0.2.1", SignInThrottle.KeyFor(System.Net.IPAddress.Parse("::ffff:192.0.2.1")));
        Assert.Equal("unknown", SignInThrottle.KeyFor(null));
    }

    [Fact]
    public async Task RotateStamp_EndsExistingSessions_ButNotKnownDevices()
    {
        var service = await WithOwnerAsync();
        var first = (await service.SignInAsync("owner", Password)).Account!;
        var token = _devices.Issue(first);

        await service.RotateStampAsync();

        Assert.False(await service.IsStampCurrentAsync(first.SecurityStamp));
        await LockAsync(service);
        Assert.Equal(SignInOutcome.Succeeded, (await service.SignInAsync("owner", Password, new SignInContext(DeviceToken: token))).Outcome);
    }

    [Fact]
    public async Task GetUserName_ReturnsTheStoredName()
    {
        Assert.Null(await NewService().GetUserNameAsync());
        var service = await WithOwnerAsync();
        Assert.Equal("Owner", await service.GetUserNameAsync());
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
