using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Users.Common;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Users.Queries;

/// <summary>USR-003 — Get user details (Admin-only). 404 when missing.</summary>
public sealed record GetUserDetailsQuery(long Id) : IRequest<Result<UserDetailResponse>>;

public sealed class GetUserDetailsQueryHandler
    : IRequestHandler<GetUserDetailsQuery, Result<UserDetailResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetUserDetailsQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<UserDetailResponse>> Handle(GetUserDetailsQuery request, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct);

        if (user is null)
            return Result<UserDetailResponse>.Failure(ErrorCodes.NotFound, "User ရှာမတွေ့ပါ။");

        // M11 tenant guard: shop admins view own-shop users only.
        if (UserManagementRules.IsCrossTenant(_currentUser.ShopId, user.ShopId))
            return Result<UserDetailResponse>.Failure(ErrorCodes.Forbidden,
                "သင့်ဆိုင်နှင့် မသက်ဆိုင်သော User ဖြစ်ပါသည် — စီမံခွင့်မရှိပါ။");

        var role = await _db.Roles.FindAsync(user.RoleId);

        return Result<UserDetailResponse>.Success(new UserDetailResponse(
            user.Id, user.Username, user.FullName, user.Phone,
            user.RoleId, role?.Code ?? "Staff", user.IsActive,
            user.LastLoginAtUtc, user.CreatedAtUtc, user.ModifiedAtUtc));
    }
}
