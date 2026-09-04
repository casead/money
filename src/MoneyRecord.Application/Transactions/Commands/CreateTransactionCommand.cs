using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;
using MoneyRecord.Domain.Entities;
using MyanmarPhone = MoneyRecord.Domain.Common.MyanmarPhone;

namespace MoneyRecord.Application.Transactions.Commands;

/// <summary>
/// TXN-001/002 shared request shape (API-007 §7). Idempotency-Key arrives via header.
/// </summary>
public abstract record CreateTxnCommand : ICommand
{
    public Guid IdempotencyKey { get; init; }
    public long? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public long WalletAccountId { get; init; }
    public long Amount { get; init; }
    public long? FeeAmountOverride { get; init; }
    public string? FeeOverrideReason { get; init; }

    /// <summary>'cash' | 'wallet' — how the fee is collected (required, BR-012 ext).</summary>
    public string FeePaidVia { get; init; } = default!;

    /// <summary>When true, fee is deducted from wallet top-up (Amount - Fee = NetAmount). Only valid with FeePaidVia='wallet'.</summary>
    public bool FeeDeductedFromAmount { get; init; }

    public string? Note { get; init; }

    /// <summary>Canonical hash for idempotency payload comparison (TC-600e).</summary>
    public string ComputeRequestHash()
    {
        var canonical = JsonSerializer.Serialize(new
        {
            customerId = CustomerId,
            customerName = CustomerName?.Trim(),
            customerPhone = CustomerPhone?.Trim(),
            walletAccountId = WalletAccountId,
            amount = Amount,
            feeAmountOverride = FeeAmountOverride,
            feePaidVia = FeePaidVia.Trim().ToLowerInvariant(),
            feeDeductedFromAmount = FeeDeductedFromAmount,
            note = Note
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public Domain.Entities.FeePaidVia ResolveFeePaidVia() =>
        FeePaidVia.Trim().ToLowerInvariant() == "wallet"
            ? Domain.Entities.FeePaidVia.WalletFloat
            : Domain.Entities.FeePaidVia.Cash;
}

public sealed record CreateCashInCommand : CreateTxnCommand, IRequest<Result<TxnReceiptResponse>>;
public sealed record CreateCashOutCommand : CreateTxnCommand, IRequest<Result<TxnReceiptResponse>>;

public sealed record TxnReceiptResponse(
    string TxnNo,
    string Status,
    long Amount,
    long FeeAmount,
    string FeePaidVia,
    long NetAmount,
    long CommissionAmount,
    bool ShowProfitFields,
    long ProfitAmount,
    BalancesAfter BalancesAfter,
    string? ReceiptUrl,
    bool DuplicateWarning,
    DateTime OccurredAtUtc,
    DateOnly BusinessDate,
    bool IsReplay);

public sealed record BalancesAfter(long CashBalance, long FloatBalance);

// ---- validation (shared rules; FR-015/016 + S-A02 phone format) ----

public abstract class CreateTxnCommandValidator<T> : AbstractValidator<T>
    where T : CreateTxnCommand
{
    protected CreateTxnCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey)
            .NotEmpty().WithMessage("Idempotency-Key header လိုအပ်ပါသည်။");

        RuleFor(x => x.CustomerName)
            .MaximumLength(100).When(x => x.CustomerName is not null)
            .WithMessage("Customer name သည် အမြင့်ဆုံး 100 လုံး ဖြစ်ရမည်။");

        RuleFor(x => x.CustomerPhone)
            .Must(p => p is null || MyanmarPhone.TryNormalize(p) is not null)
            .WithMessage("Phone သည် မြန်မာဖုန်း format (09XXXXXXXXX) ဖြစ်ရမည်။");

        RuleFor(x => x.WalletAccountId).GreaterThan(0);

        RuleFor(x => x.FeePaidVia)
            .Must(v => !string.IsNullOrWhiteSpace(v) &&
                       new[] { "cash", "wallet" }.Contains(v.Trim().ToLowerInvariant()))
            .WithMessage("feePaidVia သည် 'cash' သာမဟုတ် 'wallet' သာ ဖြစ်ရမည်။");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount သည် ၀ ထက် ကြီးရမည်။")
            .LessThanOrEqualTo(TxnRules.MaxAmount)
            .WithMessage($"Amount သည် txn တစ်ခုလျှင် အမြင့်ဆုံး {TxnRules.MaxAmount:N0} Ks သာ ဖြစ်ရမည်။ (DD-08)");

        RuleFor(x => x.FeeAmountOverride)
            .GreaterThanOrEqualTo(0).When(x => x.FeeAmountOverride is not null)
            .WithMessage("Fee override သည် အနှုတ် မဖြစ်ရ။");

        RuleFor(x => x.Note).MaximumLength(300);

        RuleFor(x => x.FeeDeductedFromAmount)
            .Must((cmd, v) => !v || cmd.FeePaidVia?.Trim().ToLowerInvariant() == "wallet")
            .WithMessage("FeeDeductedFromAmount သည် FeePaidVia='wallet' ဖြစ်မှသာ သတ်မှတ်နိုင်ပါသည်။");
    }
}

public sealed class CreateCashInCommandValidator : CreateTxnCommandValidator<CreateCashInCommand>
{
}

public sealed class CreateCashOutCommandValidator : CreateTxnCommandValidator<CreateCashOutCommand>
{
}
