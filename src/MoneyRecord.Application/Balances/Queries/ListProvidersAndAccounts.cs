using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Balances.Commands;

namespace MoneyRecord.Application.Balances.Queries;

/// <summary>PRV-001 — provider list ordered by DisplayOrder + per-provider stats.</summary>
public sealed record ListProvidersQuery(bool IncludeInactive = false)
    : IRequest<Result<List<ProviderResponse>>>;

public sealed class ListProvidersQueryHandler
    : IRequestHandler<ListProvidersQuery, Result<List<ProviderResponse>>>
{
    private readonly IMoneyRecordDbContext _db;

    public ListProvidersQueryHandler(IMoneyRecordDbContext db) => _db = db;

    public async Task<Result<List<ProviderResponse>>> Handle(ListProvidersQuery request,
        CancellationToken ct)
    {
        var query = _db.WalletProviders.AsNoTracking()
            .Where(p => !p.IsDeleted);
        if (!request.IncludeInactive)
            query = query.Where(p => p.IsActive);

        var providers = await query
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var items = new List<ProviderResponse>(providers.Count);
        foreach (var provider in providers)
            items.Add(await UpdateProviderCommandHandler.ProjectAsync(_db, provider, ct));

        return Result<List<ProviderResponse>>.Success(items);
    }
}

/// <summary>ACC-001 — wallet account list (providerId?, includeInactive?).</summary>
public sealed record ListWalletAccountsQuery(int? ProviderId, bool IncludeInactive = false)
    : IRequest<Result<List<WalletAccountResponse>>>;

public sealed class ListWalletAccountsQueryHandler
    : IRequestHandler<ListWalletAccountsQuery, Result<List<WalletAccountResponse>>>
{
    private readonly IMoneyRecordDbContext _db;

    public ListWalletAccountsQueryHandler(IMoneyRecordDbContext db) => _db = db;

    public async Task<Result<List<WalletAccountResponse>>> Handle(
        ListWalletAccountsQuery request, CancellationToken ct)
    {
        var query = _db.WalletAccounts.AsNoTracking()
            .Where(a => !a.IsDeleted);
        if (request.ProviderId is { } pid)
            query = query.Where(a => a.WalletProviderId == pid);
        if (!request.IncludeInactive)
            query = query.Where(a => a.IsActive);

        var accounts = await query
            .OrderBy(a => a.WalletProvider.DisplayOrder).ThenBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                ProviderId = a.WalletProviderId,
                ProviderCode = a.WalletProvider.Code,
                a.AccountName,
                a.AccountNumber,
                a.CurrentFloatBalance,
                a.IsActive
            })
            .ToListAsync(ct);

        return Result<List<WalletAccountResponse>>.Success(accounts
            .Select(a => new WalletAccountResponse(
                a.Id, a.ProviderId, a.ProviderCode, a.AccountName,
                CreateWalletAccountCommandHandler.Mask(a.AccountNumber),
                a.CurrentFloatBalance, a.IsActive))
            .ToList());
    }
}
