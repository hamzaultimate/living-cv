using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Data;

/// <summary>The one-account rule lives in the database: these bypass every application check.</summary>
public class OwnerAccountConstraintTests
{
    private static OwnerAccount Account(int id = OwnerAccount.SingletonId, string name = "owner") => new()
    {
        Id = id,
        UserName = name,
        PasswordHash = "hash",
        SecurityStamp = "stamp",
        CreatedUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task FirstAccount_IsAccepted()
    {
        using var test = new TestDatabase();
        using var db = test.CreateContext();

        db.OwnerAccounts.Add(Account());
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.OwnerAccounts.CountAsync());
    }

    [Fact]
    public async Task SecondAccountWithTheSameId_IsRejectedByThePrimaryKey()
    {
        using var test = new TestDatabase();
        using (var first = test.CreateContext())
        {
            first.OwnerAccounts.Add(Account());
            await first.SaveChangesAsync();
        }

        using var second = test.CreateContext();
        second.OwnerAccounts.Add(Account(name: "intruder"));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task AnyOtherId_IsRejectedByTheCheckConstraint(int id)
    {
        using var test = new TestDatabase();
        using var db = test.CreateContext();
        db.OwnerAccounts.Add(Account(id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
