using FluentAssertions;
using MoneyRecord.Domain.Common;
using MoneyRecord.Domain.Common.Exceptions;
using MoneyRecord.Domain.Entities;
using MoneyRecord.UnitTests.Common;

namespace MoneyRecord.UnitTests.Domain;

/// <summary>
/// Lockout rule tests (SEC-006 / TC-300c): 5 failed attempts → 15 min lockout.
/// </summary>
public class UserLockoutTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private static (User User, FixedClock Clock) CreateUser()
    {
        var clock = new FixedClock(Now);
        var user = User.Create("staff01", "stub::secret", "Staff One",
            roleId: 2, actorUserId: 1, clock);
        return (user, clock);
    }

    private static void FailOnce(User user, FixedClock clock)
    {
        user.VerifyLogin("wrong-password", new StubPasswordHasher("secret"), clock);
    }

    [Fact]
    public void VerifyLogin_CorrectPassword_ResetsCounterAndSetsLastLogin()
    {
        var (user, clock) = CreateUser();

        var ok = user.VerifyLogin("secret", new StubPasswordHasher("secret"), clock);

        ok.Should().BeTrue();
        user.LastLoginAtUtc.Should().Be(Now);
    }

    [Fact]
    public void VerifyLogin_FourFails_StillNotLocked()
    {
        var (user, clock) = CreateUser();

        for (var i = 0; i < AuthRules.MaxFailedLogins - 1; i++)
        {
            user.VerifyLogin("wrong", new StubPasswordHasher("secret"), clock).Should().BeFalse();
        }

        user.LockedUntilUtc.Should().BeNull();
    }

    [Fact]
    public void VerifyLogin_FifthFail_LocksAccountFor15Minutes()
    {
        var (user, clock) = CreateUser();

        for (var i = 0; i < AuthRules.MaxFailedLogins - 1; i++)
        {
            FailOnce(user, clock);
        }

        // The locking attempt itself reports bad credentials…
        var ok = user.VerifyLogin("wrong", new StubPasswordHasher("secret"), clock);

        ok.Should().BeFalse();
        // …but arms the lockout window for any further attempt.
        user.LockedUntilUtc.Should().Be(Now.AddMinutes(AuthRules.LockoutMinutes));
    }

    [Fact]
    public void VerifyLogin_WhileLocked_ThrowsEvenWithCorrectPassword()
    {
        var (user, clock) = CreateUser();
        for (var i = 0; i < AuthRules.MaxFailedLogins; i++)
        {
            try { user.VerifyLogin("wrong", new StubPasswordHasher("secret"), clock); }
            catch (AccountLockedException) { /* locked on 5th */ }
        }

        // correct password but still inside lock window
        var act = () => user.VerifyLogin("secret", new StubPasswordHasher("secret"), clock);

        act.Should().Throw<AccountLockedException>();
    }

    [Fact]
    public void VerifyLogin_LockExpired_AllowsLoginAgain()
    {
        var (user, clock) = CreateUser();
        for (var i = 0; i < AuthRules.MaxFailedLogins; i++)
        {
            try { user.VerifyLogin("wrong", new StubPasswordHasher("secret"), clock); }
            catch (AccountLockedException) { }
        }

        clock.Advance(TimeSpan.FromMinutes(AuthRules.LockoutMinutes + 1));

        var ok = user.VerifyLogin("secret", new StubPasswordHasher("secret"), clock);
        ok.Should().BeTrue();
    }

    [Fact]
    public void VerifyLogin_SuccessAfterPartialFails_ResetsCounter()
    {
        var (user, clock) = CreateUser();
        FailOnce(user, clock);
        FailOnce(user, clock);
        FailOnce(user, clock);

        user.VerifyLogin("secret", new StubPasswordHasher("secret"), clock).Should().BeTrue();

        // counter reset: 4 more fails must NOT lock
        for (var i = 0; i < AuthRules.MaxFailedLogins - 1; i++)
        {
            user.VerifyLogin("wrong", new StubPasswordHasher("secret"), clock).Should().BeFalse();
        }
        user.LockedUntilUtc.Should().BeNull();
    }

    [Fact]
    public void VerifyLogin_InactiveUser_ReturnsFalseWithoutCounting()
    {
        var (user, clock) = CreateUser();
        user.Deactivate(actorUserId: 1, clock);

        var ok = user.VerifyLogin("secret", new StubPasswordHasher("secret"), clock);

        ok.Should().BeFalse();
    }
}
