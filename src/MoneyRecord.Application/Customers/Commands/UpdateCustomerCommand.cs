using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Customers.Common;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;
using MyanmarPhone = MoneyRecord.Domain.Common.MyanmarPhone;

namespace MoneyRecord.Application.Customers.Commands;

/// <summary>
/// CUS-004 — Update customer (UC-006). Admin-only per SRS §5 matrix
/// (Staff: Create ✓ / Edit ✗). Partial body — null fields unchanged.
/// Historical transactions keep snapshots untouched (CF-03); audit before/after.
/// </summary>
public sealed record UpdateCustomerCommand(
    long Id,
    string? FullName,
    string? Phone,
    string? Address,
    string? Note) : IRequest<Result<CustomerDetailResponse>>, ICommand;

public sealed class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);

        RuleFor(x => x.FullName)
            .Length(2, 100).When(x => x.FullName is not null)
            .WithMessage("အမည်သည် 2–100 လုံး ရှိရမည်။");

        RuleFor(x => x.Phone)
            .Must(p => MyanmarPhone.TryNormalize(p) is not null)
                .When(x => x.Phone is not null)
                .WithMessage("Phone သည် မြန်မာဖုန်း format (09XXXXXXXXX) ဖြစ်ရမည်။");

        RuleFor(x => x.Address)
            .MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Address));

        RuleFor(x => x.Note)
            .MaximumLength(500).When(x => !string.IsNullOrWhiteSpace(x.Note));
    }
}

public sealed class UpdateCustomerCommandHandler
    : IRequestHandler<UpdateCustomerCommand, Result<CustomerDetailResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly ICustomerTransactionStats _stats;

    public UpdateCustomerCommandHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit, ICustomerTransactionStats stats)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _stats = stats;
    }

    public async Task<Result<CustomerDetailResponse>> Handle(UpdateCustomerCommand request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;

        // Per-shop isolation: cross-tenant ids resolve to NotFound.
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.Id
                                      && c.ShopId == _currentUser.ShopId, ct);
        if (customer is null)
            return Result<CustomerDetailResponse>.Failure(
                ErrorCodes.NotFound, "Customer ရှာမတွေ့ပါ။");

        var phone = request.Phone is null ? null : MyanmarPhone.TryNormalize(request.Phone);
        if (phone is not null && phone != customer.Phone)
        {
            // Duplicate check is shop-scoped — other shops' phones don't block.
            var duplicate = await _db.Customers
                .AnyAsync(c => c.ShopId == customer.ShopId
                               && c.Phone == phone && c.Id != customer.Id, ct);
            if (duplicate)
                return Result<CustomerDetailResponse>.FailureWith(
                    ErrorCodes.Duplicate,
                    $"ဤ Phone ({phone}) ဖြင့် Customer ရှိပြီးသား ဖြစ်နေပါသည်။",
                    new Dictionary<string, object?>
                    {
                        ["existingCustomerId"] =
                            (await _db.Customers
                                .FirstAsync(c => c.ShopId == customer.ShopId
                                                 && c.Phone == phone
                                                 && c.Id != customer.Id, ct)).Id
                    });
        }

        var before = System.Text.Json.JsonSerializer.Serialize(new
        { customer.FullName, customer.Phone, customer.Address, customer.Note });

        customer.UpdateProfile(request.FullName, phone, request.Address, request.Note,
            actorId, _clock);

        var after = System.Text.Json.JsonSerializer.Serialize(new
        { customer.FullName, customer.Phone, customer.Address, customer.Note });

        await _audit.LogAsync("CUSTOMER.UPDATE", "Customer", customer.Id.ToString(),
            oldValue: before, newValue: after, ct: ct);

        await _db.SaveChangesAsync(ct);

        var stats = await _stats.GetAsync(customer.Id, ct);
        return Result<CustomerDetailResponse>.Success(
            CustomerMapping.ToResponse(customer, stats));
    }
}
