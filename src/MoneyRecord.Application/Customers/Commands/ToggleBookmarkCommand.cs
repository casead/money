using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Application.Customers.Commands;

/// <summary>Toggle customer bookmark for quick access in Bookmark tab.</summary>
public sealed record ToggleBookmarkCommand(
    long Id,
    bool IsBookmarked) : IRequest<Result<BookmarkResponse>>, ICommand;

public sealed class ToggleBookmarkCommandValidator : AbstractValidator<ToggleBookmarkCommand>
{
    public ToggleBookmarkCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public sealed record BookmarkResponse(long Id, bool IsBookmarked);

public sealed class ToggleBookmarkCommandHandler
    : IRequestHandler<ToggleBookmarkCommand, Result<BookmarkResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ToggleBookmarkCommandHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<BookmarkResponse>> Handle(ToggleBookmarkCommand request,
        CancellationToken ct)
    {
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.Id
                                      && c.ShopId == _currentUser.ShopId, ct);
        if (customer is null)
            return Result<BookmarkResponse>.Failure(
                ErrorCodes.NotFound, "Customer ရှာမတွေ့ပါ။");

        customer.SetBookmarked(request.IsBookmarked);
        await _db.SaveChangesAsync(ct);

        return Result<BookmarkResponse>.Success(
            new BookmarkResponse(customer.Id, customer.IsBookmarked));
    }
}
