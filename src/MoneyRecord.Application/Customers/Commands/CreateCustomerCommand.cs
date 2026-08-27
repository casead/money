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
/// CUS-002 — Create customer (UC-005, FR-010). A and S.
/// Phone normalized to canonical; duplicate active phone → 409 DUPLICATE + existingId
/// (client shows "existing customer" suggestion). Audit CUSTOMER.CREATE.
/// </summary>
public sealed record CreateCustomerCommand(
    string FullName,
    string Phone,
    string? Address,
    string? Note) : IRequest<Result<CustomerDetailResponse>>, ICommand;

public sealed class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("အမည် လိုအပ်ပါသည်။")
            .Length(2, 100).WithMessage("အမည်သည် 2–100 လုံး ရှိရမည်။");

        RuleFor(x => x.Phone)
            .Must(p => MyanmarPhone.TryNormalize(p) is not null)
                .WithMessage("Phone သည် မြန်မာဖုန်း format (09XXXXXXXXX) ဖြစ်ရမည်။");

        RuleFor(x => x.Address)
            .MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Address))
            .WithMessage("Address သည် 200 လုံးအောက် ရှိရမည်။");

        RuleFor(x => x.Note)
            .MaximumLength(500).When(x => !string.IsNullOrWhiteSpace(x.Note))
            .WithMessage("Note သည် 500 လုံးအောက် ရှိရမည်။");
    }
}

public sealed class CreateCustomerCommandHandler
    : IRequestHandler<CreateCustomerCommand, Result<CustomerDetailResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly ICustomerTransactionStats _stats;

    public CreateCustomerCommandHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit, ICustomerTransactionStats stats)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _stats = stats;
    }

    public async Task<Result<CustomerDetailResponse>> Handle(CreateCustomerCommand request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;
        var phone = MyanmarPhone.TryNormalize(request.Phone)!;

        // Per-shop tenancy: only shop-scoped accounts may own customers.
        if (_currentUser.ShopId is null)
            return Result<CustomerDetailResponse>.Failure(
                ErrorCodes.Forbidden,
                "Customer registry ကို ဆိုင်အကောင့် (Shop Admin/Staff) များသာ ထည့်နိုင်ပါသည်။");
        var shopId = _currentUser.ShopId.Value;

        // Duplicate check is shop-scoped — the same phone may exist in another shop.
        var existing = await _db.Customers.IgnoreQueryFilters()
            .Where(c => c.ShopId == shopId && c.Phone == phone && !c.IsDeleted)
            .Select(c => (long?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return Result<CustomerDetailResponse>.FailureWith(
                ErrorCodes.Duplicate,
                $"ဤ Phone ({phone}) ဖြင့် Customer ရှိပြီးသား ဖြစ်နေပါသည်။",
                new Dictionary<string, object?> { ["existingCustomerId"] = existing.Value });

        var customer = Customer.Create(
            request.FullName, phone, request.Address, request.Note, actorId, _clock,
            shopId);

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct); // id lands in audit row; same TxBehavior txn

        await _audit.LogAsync("CUSTOMER.CREATE", "Customer", customer.Id.ToString(),
            newValue: System.Text.Json.JsonSerializer.Serialize(new
            {
                customer.Id, customer.FullName, customer.Phone
            }), ct: ct);
        await _db.SaveChangesAsync(ct);

        var stats = await _stats.GetAsync(customer.Id, ct);
        return Result<CustomerDetailResponse>.Success(
            CustomerMapping.ToResponse(customer, stats));
    }
}
