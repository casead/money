using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Transactions.Commands;

public sealed record ConfirmCreditCommand(string TxnNo) : IRequest<Result<string>>;

public sealed class ConfirmCreditHandler : IRequestHandler<ConfirmCreditCommand, Result<string>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public ConfirmCreditHandler(IMoneyRecordDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<string>> Handle(ConfirmCreditCommand request, CancellationToken ct)
    {
        var txn = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TxnNo == request.TxnNo && t.ShopId == _currentUser.ShopId, ct);

        if (txn is null)
            return Result<string>.Failure(ErrorCodes.NotFound, "Transaction ရှာမတွေ့ပါ။");
        if (!txn.IsCredit)
            return Result<string>.Failure(ErrorCodes.InvalidOperation, "Credit transaction မဟုတ်ပါ။");

        txn.ConfirmCredit(_currentUser.UserId ?? 0, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);

        return Result<string>.Success("Confirmed");
    }
}