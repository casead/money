using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Users.Common;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Application.Users.Queries;

/// <summary>USR-003 — Get user details (Admin-only). 404 when missing.</summary>
public sealed record GetUserDetailsQuery(long Id) : IRequest<Result<UserDetailResponse>>;

public sealed class GetUserDetailsQueryHandler
    : IRequestHandler<GetUserDetailsQuery, Result<UserDetailResponse>>
{
    private readonly IMoneyRecordDbContext _db;

    public GetUserDetailsQueryHandler(IMoneyRecordDbContext db) => _db = db;

    public async Task<Result<UserDetailResponse>> Handle(GetUserDetailsQuery request, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct);

        if (user is null)
            return Result<UserDetailResponse>.Failure(ErrorCodes.NotFound, "User ရှာမတွေ့ပါ။");

        var role = await _db.Roles.FindAsync(user.RoleId);

        return Result<UserDetailResponse>.Success(new UserDetailResponse(
            user.Id, user.Username, user.FullName, user.Phone,
            user.RoleId, role?.Code ?? "Staff", user.IsActive,
            user.LastLoginAtUtc, user.CreatedAtUtc, user.ModifiedAtUtc));
    }
}
