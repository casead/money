using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Fees.Commands;
using MoneyRecord.Application.Fees.Services;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Fees.Queries;

// ============================================================================
// FEE-001 — List Fee Rules (A, S read) — providerId?, activeOnly?, asOfDate?
// ============================================================================

public sealed record ListFeeRulesQuery(int? ProviderId, bool ActiveOnly,
    DateOnly? AsOfDate) : IRequest<Result<List<FeeRuleResponse>>>;

public sealed class ListFeeRulesQueryHandler
    : IRequestHandler<ListFeeRulesQuery, Result<List<FeeRuleResponse>>>
{
    private readonly IMoneyRecordDbContext _db;

    public ListFeeRulesQueryHandler(IMoneyRecordDbContext db) => _db = db;

    public async Task<Result<List<FeeRuleResponse>>> Handle(ListFeeRulesQuery request,
        CancellationToken ct)
    {
        var asOfUtc = request.AsOfDate is { } d
            ? CreateFeeRuleCommandHandler.ToUtc(d)
            : DateTime.MaxValue;

        var query = _db.FeeRules.AsNoTracking()
            .OrderByDescending(r => r.EffectiveFromUtc)
            .AsQueryable();

        if (request.ProviderId is { } pid)
            query = query.Where(r => r.WalletProviderId == pid);
        if (request.ActiveOnly)
            query = query.Where(r => r.IsActive &&
                r.EffectiveFromUtc <= asOfUtc &&
                (r.EffectiveToUtc == null || r.EffectiveToUtc > asOfUtc));

        var items = await query.ToListAsync(ct);

        var providerIds = items.Select(r => r.WalletProviderId).Distinct().ToList();
        var providers = await _db.WalletProviders
            .Where(p => providerIds.Contains(p.Id))
            .ToListAsync(ct);
        var providerDict = providers.ToDictionary(p => p.Id);

        return Result<List<FeeRuleResponse>>.Success(items
            .Select(r => CreateFeeRuleCommandHandler.ToResponse(r,
                providerDict.TryGetValue(r.WalletProviderId, out var wp) ? wp.Code : "UNKNOWN"))
            .ToList());
    }
}

// ============================================================================
// FEE-004 — Preview Fee Calculation (entry-screen live display, FR-028).
// Percent-only engine v2 — resolved from the CashIn/CashOut settings rates.
// ============================================================================

public sealed record PreviewFeeQuery(TransactionType TxnType, long Amount)
    : IRequest<Result<PreviewFeeResponse>>;

public sealed record PreviewFeeResponse(long FeeAmount, long NetAmount, decimal? PercentApplied);

public sealed class PreviewFeeQueryValidator : FluentValidation.AbstractValidator<PreviewFeeQuery>
{
    public PreviewFeeQueryValidator()
    {
        RuleFor(x => x.TxnType).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class PreviewFeeQueryHandler
    : IRequestHandler<PreviewFeeQuery, Result<PreviewFeeResponse>>
{
    private readonly IFeeCalculator _calculator;

    public PreviewFeeQueryHandler(IFeeCalculator calculator) => _calculator = calculator;

    public async Task<Result<PreviewFeeResponse>> Handle(PreviewFeeQuery request,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
            return Result<PreviewFeeResponse>.Failure(ErrorCodes.InvalidOperation,
                "Amount သည် ၀ ထက် ကြီးရမည်။");

        var resolution = await _calculator.CalculateAsync(
            request.TxnType, request.Amount, ct);

        return Result<PreviewFeeResponse>.Success(new PreviewFeeResponse(
            resolution.FeeAmount, request.Amount,
            PercentApplied: null));
    }
}
