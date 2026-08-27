using MediatR;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Fees.Services;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Transactions.Commands;

/// <summary>
/// TXN-001 — Cash In (BR-010): customer cash ↑ shop, float ↓ provider account.
/// Lock order: WALLET first, then CASH (BRL §19 deadlock rule).
/// </summary>
public sealed class CreateCashInHandler : CreateTxnHandlerBase<CreateCashInCommand>,
    IRequestHandler<CreateCashInCommand, Result<TxnReceiptResponse>>
{
    public CreateCashInHandler(IMoneyRecordDbContext db, IBalanceLocker locker,
        IIdempotencyStore idempotency, ITxnNumberGenerator txnNumbers,
        IFeeCalculator feeCalculator, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit)
        : base(db, locker, idempotency, txnNumbers, feeCalculator, clock, currentUser, audit)
    {
    }

    protected override TransactionType Type => TransactionType.CashIn;
    protected override string ActionCode => "TXN.CREATE_CASH_IN";
}

/// <summary>
/// TXN-002 — Cash Out (BR-011): cash ↓ shop, float ↑ provider account.
/// Lock order: CASH FIRST, then WALLET — sufficiency checked on physical cash (BR-032).
/// </summary>
public sealed class CreateCashOutHandler : CreateTxnHandlerBase<CreateCashOutCommand>,
    IRequestHandler<CreateCashOutCommand, Result<TxnReceiptResponse>>
{
    public CreateCashOutHandler(IMoneyRecordDbContext db, IBalanceLocker locker,
        IIdempotencyStore idempotency, ITxnNumberGenerator txnNumbers,
        IFeeCalculator feeCalculator, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit)
        : base(db, locker, idempotency, txnNumbers, feeCalculator, clock, currentUser, audit)
    {
    }

    protected override TransactionType Type => TransactionType.CashOut;
    protected override string ActionCode => "TXN.CREATE_CASH_OUT";
}
