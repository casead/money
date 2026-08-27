using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Settings.Commands;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.Application.Settings.Queries;

/// <summary>
/// SET-001 — settings read model. Admin sees all keys; Staff only the safe subset.
/// </summary>
public sealed record GetSettingsQuery : IRequest<Result<SettingsResponse>>;

public sealed record SettingsValue(string Key, string Value, string ValueType);

public sealed record SettingsResponse(IReadOnlyList<SettingsValue> Values);

public sealed class GetSettingsQueryHandler
    : IRequestHandler<GetSettingsQuery, Result<SettingsResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetSettingsQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<SettingsResponse>> Handle(GetSettingsQuery request,
        CancellationToken ct)
    {
        var isAdmin = _currentUser.RoleId == RolePermissionRegistry.AdminRoleId;

        // M11: only this shop's overrides + platform defaults; collapse to the
        // effective value per key (shop override wins).
        var query = _db.AppSettings.AsNoTracking()
            .Where(s => s.ShopId == null || s.ShopId == _currentUser.ShopId)
            .OrderBy(s => s.Id);
        var rows = (isAdmin
                ? await query.ToListAsync(ct)
                : await query.Where(s => SettingCatalog.StaffSafeKeys.Contains(s.Key))
                    .ToListAsync(ct))
            .GroupBy(s => s.Key)
            .Select(g => g.OrderByDescending(s => s.ShopId != null).First())
            .OrderBy(s => s.Id)
            .ToList();

        return Result<SettingsResponse>.Success(new SettingsResponse(
            rows.Select(s => new SettingsValue(s.Key, s.Value, s.ValueType)).ToList()));
    }
}
