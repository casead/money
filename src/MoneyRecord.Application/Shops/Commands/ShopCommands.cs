using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Shops.Commands;

/// <summary>TEN-001 — Create shop (tenant.manage / SuperAdmin).</summary>
public sealed record CreateShopCommand(string Code, string Name)
    : IRequest<Result<ShopResponse>>, ICommand;

/// <summary>TEN-002 — Rename shop.</summary>
public sealed record UpdateShopCommand(long Id, string Name)
    : IRequest<Result<ShopResponse>>, ICommand;

/// <summary>TEN-003 — Suspend/reactivate shop. Suspended blocks member logins (M11).</summary>
public sealed record SetShopStatusCommand(long Id, bool IsActive)
    : IRequest<Result<ShopResponse>>, ICommand;

public sealed record ShopResponse(
    long Id, string Code, string Name, int Status,
    DateTime CreatedAtUtc);

public sealed class CreateShopCommandHandler : IRequestHandler<CreateShopCommand, Result<ShopResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public CreateShopCommandHandler(IMoneyRecordDbContext db, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<ShopResponse>> Handle(CreateShopCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Shops.AnyAsync(s => s.Code == code, ct))
            return Result<ShopResponse>.Failure(ErrorCodes.Duplicate,
                $"Shop code '{code}' ကို အသုံးပြုထားပြီးသားပါ။");

        var shop = Shop.Create(code, request.Name, _clock);
        _db.Shops.Add(shop);
        await _db.SaveChangesAsync(ct); // Shop.Id generated before the cash-pool row

        // Every tenant gets its own physical-cash pool row (Id = ShopId) at
        // creation time — dashboards/cash endpoints read this row per shop.
        _db.PhysicalCashAccounts.Add(
            Domain.Entities.PhysicalCashAccount.CreateForShop(shop.Id, 0, _clock));

        await _audit.LogAsync("SHOP.CREATE", "Shop", shop.Id.ToString(),
            newValue: System.Text.Json.JsonSerializer.Serialize(
                new { shop.Code, shop.Name }), ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<ShopResponse>.Success(new ShopResponse(
            shop.Id, shop.Code, shop.Name, shop.Status, shop.CreatedAtUtc));
    }
}

public sealed class UpdateShopCommandHandler : IRequestHandler<UpdateShopCommand, Result<ShopResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public UpdateShopCommandHandler(IMoneyRecordDbContext db, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<ShopResponse>> Handle(UpdateShopCommand request, CancellationToken ct)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == request.Id, ct);
        if (shop is null)
            return Result<ShopResponse>.Failure(ErrorCodes.NotFound, "ဆိုင် ရှာမတွေ့ပါ။");

        var before = shop.Name;
        shop.Rename(request.Name, _clock);

        await _audit.LogAsync("SHOP.UPDATE", "Shop", shop.Id.ToString(),
            oldValue: before, newValue: shop.Name, ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<ShopResponse>.Success(new ShopResponse(
            shop.Id, shop.Code, shop.Name, shop.Status, shop.CreatedAtUtc));
    }
}

public sealed class SetShopStatusCommandHandler
    : IRequestHandler<SetShopStatusCommand, Result<ShopResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public SetShopStatusCommandHandler(IMoneyRecordDbContext db, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<ShopResponse>> Handle(SetShopStatusCommand request, CancellationToken ct)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == request.Id, ct);
        if (shop is null)
            return Result<ShopResponse>.Failure(ErrorCodes.NotFound, "ဆိုင် ရှာမတွေ့ပါ။");

        if (request.IsActive) shop.Reactivate(_clock);
        else shop.Suspend(_clock);

        // Audit AFTER save so the row carries the final status; same TxBehavior txn.
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(request.IsActive ? "SHOP.REACTIVATE" : "SHOP.SUSPEND",
            "Shop", shop.Id.ToString(),
            newValue: $"status={shop.Status}", ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<ShopResponse>.Success(new ShopResponse(
            shop.Id, shop.Code, shop.Name, shop.Status, shop.CreatedAtUtc));
    }
}
