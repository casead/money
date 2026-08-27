using FluentAssertions;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.UnitTests.Domain;

/// <summary>
/// TC-300c/d extensions (Module 3): self-role-change, self-deactivate and
/// last-active-Admin guards (FR-007 / UC-004).
/// </summary>
public class UserManagementRulesTests
{
    private const long AdminId = 10;
    private const long OtherUserId = 20;

    // ---- Role change guards ----

    [Fact]
    public void RoleChange_OnSelf_IsBlocked()
    {
        var error = UserManagementRules.CheckRoleChange(AdminId, AdminId,
            demotesLastActiveAdmin: false);

        error.Should().Be(ErrorCodes.SelfRoleChange);
    }

    [Fact]
    public void RoleChange_OnOtherUser_WithAdminsRemaining_IsAllowed()
    {
        var error = UserManagementRules.CheckRoleChange(AdminId, OtherUserId,
            demotesLastActiveAdmin: false);

        error.Should().BeNull();
    }

    [Fact]
    public void Demotion_Of_LastActiveAdmin_IsBlocked_EvenByOtherAdmin()
    {
        var error = UserManagementRules.CheckRoleChange(OtherUserId, AdminId,
            demotesLastActiveAdmin: true);

        error.Should().Be(ErrorCodes.LastAdmin);
    }

    [Fact]
    public void IsLastActiveAdmin_True_OnlyForAdminRoleWithZeroOthers()
    {
        UserManagementRules.IsLastActiveAdmin(activeAdminCountExcludingTarget: 0,
                targetRoleId: RolePermissionRegistry.AdminRoleId)
            .Should().BeTrue();

        UserManagementRules.IsLastActiveAdmin(activeAdminCountExcludingTarget: 2,
                targetRoleId: RolePermissionRegistry.AdminRoleId)
            .Should().BeFalse();

        // demoting a Staff user can never remove the last admin
        UserManagementRules.IsLastActiveAdmin(activeAdminCountExcludingTarget: 0,
                targetRoleId: RolePermissionRegistry.StaffRoleId)
            .Should().BeFalse();
    }

    // ---- Role assignment guards (USR-002/004: only SuperAdmin grants Admin) ----

    [Fact]
    public void ShopAdmin_Cannot_Grant_AdminRole()
    {
        var error = UserManagementRules.CheckRoleAssignment(
            actorRoleId: RolePermissionRegistry.AdminRoleId,
            requestedRoleId: RolePermissionRegistry.AdminRoleId);

        error.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public void StaffOrUnknownActor_Cannot_Grant_AdminRole()
    {
        UserManagementRules.CheckRoleAssignment(
                actorRoleId: RolePermissionRegistry.StaffRoleId,
                requestedRoleId: RolePermissionRegistry.AdminRoleId)
            .Should().Be(ErrorCodes.Forbidden);

        // missing role claim must never be able to mint an Admin
        UserManagementRules.CheckRoleAssignment(
                actorRoleId: null,
                requestedRoleId: RolePermissionRegistry.AdminRoleId)
            .Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public void SuperAdmin_Can_Grant_AdminRole()
    {
        var error = UserManagementRules.CheckRoleAssignment(
            actorRoleId: RolePermissionRegistry.SuperAdminRoleId,
            requestedRoleId: RolePermissionRegistry.AdminRoleId);

        error.Should().BeNull();
    }

    [Theory]
    [InlineData(2)] // ShopAdmin actor
    [InlineData(1)] // SuperAdmin actor
    public void Creating_Staff_IsAlwaysAllowed(int actorRoleId)
    {
        var error = UserManagementRules.CheckRoleAssignment(
            actorRoleId: actorRoleId,
            requestedRoleId: RolePermissionRegistry.StaffRoleId);

        error.Should().BeNull();
    }

    // ---- Status change guards ----

    [Fact]
    public void Deactivate_OnSelf_IsBlocked()
    {
        var error = UserManagementRules.CheckStatusChange(AdminId, AdminId,
            deactivating: true, targetIsLastActiveAdmin: false);

        error.Should().Be(ErrorCodes.SelfDeactivate);
    }

    [Fact]
    public void Deactivate_LastActiveAdmin_IsBlocked()
    {
        var error = UserManagementRules.CheckStatusChange(OtherUserId, AdminId,
            deactivating: true, targetIsLastActiveAdmin: true);

        error.Should().Be(ErrorCodes.LastAdmin);
    }

    [Fact]
    public void Deactivate_NormalStaff_ByAdmin_IsAllowed()
    {
        var error = UserManagementRules.CheckStatusChange(AdminId, OtherUserId,
            deactivating: true, targetIsLastActiveAdmin: false);

        error.Should().BeNull();
    }

    [Fact]
    public void Reactivation_Never_Triggers_LastAdminGuard()
    {
        var error = UserManagementRules.CheckStatusChange(OtherUserId, AdminId,
            deactivating: false, targetIsLastActiveAdmin: true);

        error.Should().BeNull();
    }
}
