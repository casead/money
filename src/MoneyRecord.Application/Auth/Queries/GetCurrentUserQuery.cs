using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Auth.Queries;

/// <summary>AUTH-004 — current user profile from JWT subject (app bootstrap).</summary>
public sealed record GetCurrentUserQuery : IRequest<Result<MeResponse>>;

public sealed record MeResponse(
    long Id,
    string Username,
    string FullName,
    string RoleCode,
    string[] Permissions,
    string ShopName);

public sealed class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, Result<MeResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetCurrentUserQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<MeResponse>> Handle(GetCurrentUserQuery request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result<MeResponse>.Failure(ErrorCodes.Unauthorized, "Login ဝင်ပါ။");

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && !u.IsDeleted, ct);

        if (user is null)
            return Result<MeResponse>.Failure(ErrorCodes.Unauthorized,
                "Account ရှာမတွေ့ပါ သို့မဟုတ် ရပ်တန့်ထားပါသည်။");

        var role = await _db.Roles.FindAsync(user.RoleId);
        var shop = user.ShopId.HasValue ? await _db.Shops.FindAsync(user.ShopId) : null;

        // v1 permission model: code-level registry (single source of truth, DBD T03 note).
        var permissions = RolePermissionRegistry.Ordered(user.RoleId);

        return Result<MeResponse>.Success(new MeResponse(
            user.Id,
            user.Username,
            user.FullName,
            role?.Code ?? "Staff",
            permissions,
            ShopName: shop?.Name ?? "Platform"));
    }
}
