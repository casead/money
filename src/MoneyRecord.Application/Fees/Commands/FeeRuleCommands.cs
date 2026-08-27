using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Fees.Services;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Fees.Commands;

// ============================================================================
// FEE-002 — Create Fee Rule (fee.manage, Admin-only; overlap → 409 OVERLAP_RULE)
// ============================================================================

public sealed record CreateFeeRuleCommand(
    int ProviderId,
    byte CalculationType,
    long? FlatFee,
    decimal? PercentRate,
    long? MinFee,
    long? MaxFee,
    DateOnly EffectiveFrom) : IRequest<Result<FeeRuleResponse>>, ICommand;

public sealed record FeeRuleResponse(
    int Id,
    int ProviderId,
    string ProviderCode,
    byte CalculationType,
    long? FlatFee,
    decimal? PercentRate,
    long? MinFee,
    long? MaxFee,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive);

public sealed class CreateFeeRuleCommandValidator : AbstractValidator<CreateFeeRuleCommand>
{
    public CreateFeeRuleCommandValidator()
    {
        RuleFor(x => x.ProviderId).GreaterThan(0);
        RuleFor(x => x.CalculationType)
            .InclusiveBetween((byte)1, (byte)3)
            .WithMessage("CalculationType သည် 1=FLAT | 2=PERCENT | 3=TIERED သာ ဖြစ်ရမည်။");
        RuleFor(x => x.FlatFee).NotNull().GreaterThan(0)
            .When(x => x.CalculationType == 1)
            .WithMessage("FLAT rule အတွက် flatFee > 0 လိုအပ်ပါသည်။");
        RuleFor(x => x.PercentRate).NotNull()
            .Must(p => p > 0 && p <= 100)
            .When(x => x.CalculationType == 2)
            .WithMessage("PERCENT rule အတွက် percentRate သည် (0,100] အတွင်း ရှိရမည်။");
        RuleFor(x => x.MaxFee).GreaterThanOrEqualTo(x => x.MinFee ?? 0)
            .When(x => x.MaxFee is not null);
    }
}

public sealed class CreateFeeRuleCommandHandler
    : IRequestHandler<CreateFeeRuleCommand, Result<FeeRuleResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public CreateFeeRuleCommandHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<FeeRuleResponse>> Handle(CreateFeeRuleCommand request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;

        var provider = await _db.WalletProviders.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProviderId, ct);
        if (provider is null)
            return Result<FeeRuleResponse>.Failure(ErrorCodes.NotFound,
                "Provider ရှာမတွေ့ပါ။");

        // FEE-009: effectiveFrom ≥ today (Yangon) for NEW rules.
        if (request.EffectiveFrom < _clock.TodayYangon)
            return Result<FeeRuleResponse>.Failure(ErrorCodes.InvalidOperation,
                "effectiveFrom သည် ဒီနေ့ (Yangon) ထက် စောလို့မရပါ။");

        var fromUtc = ToUtc(request.EffectiveFrom);

        // ---- Overlap check (BR-013): same provider, active, intersecting window.
        //      Serialized via UPDLOCK range read inside the TxBehavior transaction. ----
        var overlap = await _db.FeeRules.AsNoTracking()
            .AnyAsync(r => r.WalletProviderId == request.ProviderId &&
                           r.IsActive &&
                           r.EffectiveFromUtc < DateTime.MaxValue &&
                           (r.EffectiveToUtc ?? DateTime.MaxValue) > fromUtc, ct);
        if (overlap)
            return Result<FeeRuleResponse>.FailureWith(
                ErrorCodes.Duplicate,
                "ဤ provider တွင် ထိုးသွင်းကာလ ထပ်နေသော rule ရှိနေပါသည်။",
                new Dictionary<string, object?> { ["reason"] = "OVERLAP_RULE" });

        var rule = Domain.Entities.FeeRule.Create(
            request.ProviderId, (FeeCalculationType)request.CalculationType,
            request.FlatFee, request.PercentRate, request.MinFee, request.MaxFee,
            fromUtc, actorId, _clock);

        _db.FeeRules.Add(rule);
        await _db.SaveChangesAsync(ct); // materializes Id

        await _audit.LogAsync("FEE.RULE.CREATE", "FeeRule", rule.Id.ToString(),
            newValue: System.Text.Json.JsonSerializer.Serialize(new
            {
                rule.Id, provider = provider.Code,
                type = rule.CalculationType.ToString(),
                rule.FlatAmount, rule.PercentValue, rule.MinFee, rule.MaxFee,
                from = rule.EffectiveFromUtc
            }), ct: ct);

        return Result<FeeRuleResponse>.Success(ToResponse(rule, provider.Code));
    }

    internal static Result<FeeRuleResponse> NotFound() =>
        Result<FeeRuleResponse>.Failure(ErrorCodes.NotFound, "Fee rule ရှာမတွေ့ပါ။");

    internal static FeeRuleResponse ToResponse(FeeRule r, string providerCode) =>
        new(r.Id, r.WalletProviderId, providerCode, (byte)r.CalculationType,
            r.FlatAmount, r.PercentValue, r.MinFee, r.MaxFee,
            DateOnly.FromDateTime(r.EffectiveFromUtc),
            r.EffectiveToUtc is null ? null : DateOnly.FromDateTime(r.EffectiveToUtc.Value),
            r.IsActive);

    internal static DateTime ToUtc(DateOnly yangonDate) =>
        new DateTime(yangonDate, TimeOnly.MinValue, DateTimeKind.Unspecified)
            .AddHours(-6.5).ToUniversalTime();
}

// ============================================================================
// FEE-003 — Update Fee Rule (only NOT-YET-EFFECTIVE rules editable → IMMUTABLE_RULE)
// ============================================================================

public sealed record UpdateFeeRuleCommand(
    int Id,
    long? FlatFee,
    decimal? PercentRate,
    long? MinFee,
    long? MaxFee,
    DateOnly? EffectiveFrom) : IRequest<Result<FeeRuleResponse>>, ICommand;

public sealed class UpdateFeeRuleCommandValidator : AbstractValidator<UpdateFeeRuleCommand>
{
    public UpdateFeeRuleCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.FlatFee).NotNull().GreaterThan(0)
            .When(x => x.FlatFee is not null);
        RuleFor(x => x.PercentRate)
            .Must(p => p > 0 && p <= 100)
            .When(x => x.PercentRate is not null);
    }
}

public sealed class UpdateFeeRuleCommandHandler
    : IRequestHandler<UpdateFeeRuleCommand, Result<FeeRuleResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public UpdateFeeRuleCommandHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<FeeRuleResponse>> Handle(UpdateFeeRuleCommand request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;
        var utcNow = _clock.UtcNow;

        var rule = await _db.FeeRules
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct);
        if (rule is null)
            return CreateFeeRuleCommandHandler.NotFound();

        var beforeJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            rule.FlatAmount, rule.PercentValue, rule.MinFee, rule.MaxFee,
            rule.EffectiveFromUtc
        });

        try
        {
            if (request.FlatFee is not null || request.PercentRate is not null ||
                request.MinFee is not null || request.MaxFee is not null)
            {
                rule.ReviseParameters(request.FlatFee, request.PercentRate,
                    request.MinFee, request.MaxFee, actorId, utcNow);
            }

            if (request.EffectiveFrom is { } eff)
            {
                if (eff < _clock.TodayYangon)
                    return Result<FeeRuleResponse>.Failure(ErrorCodes.InvalidOperation,
                        "effectiveFrom သည် ဒီနေ့ (Yangon) ထက် စောလို့မရပါ။");
                var newFrom = CreateFeeRuleCommandHandler.ToUtc(eff);
                rule.Reschedule(newFrom, rule.EffectiveToUtc, actorId, utcNow);
            }
        }
        catch (ConflictStateException)
        {
            return Result<FeeRuleResponse>.FailureWith(
                ErrorCodes.ConflictState,
                "အာဏာတည်ဆဲ rule ကို ပြင်ခွင့် မရှိပါ။",
                new Dictionary<string, object?> { ["reason"] = "IMMUTABLE_RULE" });
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("FEE.RULE.UPDATE", "FeeRule", rule.Id.ToString(),
            oldValue: beforeJson,
            newValue: System.Text.Json.JsonSerializer.Serialize(new
            {
                rule.FlatAmount, rule.PercentValue, rule.MinFee, rule.MaxFee,
                rule.EffectiveFromUtc
            }), ct: ct);

        var providerCode = (await _db.WalletProviders.AsNoTracking()
            .FirstAsync(p => p.Id == rule.WalletProviderId, ct)).Code;

        return Result<FeeRuleResponse>.Success(
            CreateFeeRuleCommandHandler.ToResponse(rule, providerCode));
    }
}
