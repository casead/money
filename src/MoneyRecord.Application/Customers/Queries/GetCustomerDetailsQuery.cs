using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Customers.Common;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Application.Customers.Queries;

/// <summary>CUS-003 — full profile + lifetime aggregates (A, S). Shop-scoped.</summary>
public sealed record GetCustomerDetailsQuery(long Id) : IRequest<Result<CustomerDetailResponse>>;

public sealed class GetCustomerDetailsQueryHandler
    : IRequestHandler<GetCustomerDetailsQuery, Result<CustomerDetailResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerTransactionStats _stats;

    public GetCustomerDetailsQueryHandler(IMoneyRecordDbContext db,
        ICurrentUser currentUser, ICustomerTransactionStats stats)
    {
        _db = db;
        _currentUser = currentUser;
        _stats = stats;
    }

    public async Task<Result<CustomerDetailResponse>> Handle(GetCustomerDetailsQuery request,
        CancellationToken ct)
    {
        // Per-shop isolation: cross-tenant ids resolve to NotFound.
        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.Id
                                      && c.ShopId == _currentUser.ShopId, ct);

        if (customer is null)
            return Result<CustomerDetailResponse>.Failure(
                ErrorCodes.NotFound, "Customer ရှာမတွေ့ပါ။");

        var stats = await _stats.GetAsync(customer.Id, ct);
        return Result<CustomerDetailResponse>.Success(
            CustomerMapping.ToResponse(customer, stats));
    }
}
