using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Audit.Queries;

/// <summary>
/// AUD-001 — read-only, paginated audit trail (Admin only via audit.view policy).
/// Filters: date window (UTC), entityType, entityId, actionCode fragment, actor.
/// PII is masked per SEC-008 — values are already masked at write time; this query
/// never exposes raw actor credentials because none are stored (DR-04 append-only).
/// </summary>
public sealed record ListAuditLogsQuery(
    int Page,
    int PageSize,
    DateTime? DateFrom,
    DateTime? DateTo,
    string? EntityType,
    string? EntityId,
    string? Action,
    long? ActorUserId) : IRequest<Result<PagedResult<AuditLogRow>>>;

public sealed record AuditLogRow(
    long Id,
    DateTime OccurredAtUtc,
    string? ActorUserName,
    string ActionCode,
    string EntityType,
    string EntityId,
    string? OldValuesJson,
    string? NewValuesJson);

public sealed class ListAuditLogsQueryValidator : AbstractValidator<ListAuditLogsQuery>
{
    public ListAuditLogsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PagedResult<AuditLogRow>.MaxPageSize);
        RuleFor(x => x.EntityType)
            .MaximumLength(30);
        RuleFor(x => x.Action)
            .MaximumLength(40);
    }
}

public sealed class ListAuditLogsQueryHandler
    : IRequestHandler<ListAuditLogsQuery, Result<PagedResult<AuditLogRow>>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListAuditLogsQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<AuditLogRow>>> Handle(
        ListAuditLogsQuery request, CancellationToken ct)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > PagedResult<AuditLogRow>.MaxPageSize
            ? PagedResult<AuditLogRow>.DefaultPageSize
            : request.PageSize;

        // Tenant scope (M11): ShopAdmin sees own shop; SuperAdmin (ShopId=null)
        // sees platform-level rows only.
        var query = _db.AuditLogs.AsNoTracking()
            .Where(a => a.ShopId == _currentUser.ShopId);

        if (request.DateFrom is { } from)
            query = query.Where(a => a.CreatedAtUtc >= from);
        if (request.DateTo is { } to)
            query = query.Where(a => a.CreatedAtUtc <= to);
        if (!string.IsNullOrWhiteSpace(request.EntityType))
            query = query.Where(a => a.EntityType == request.EntityType.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(request.EntityId))
            query = query.Where(a => a.EntityId == request.EntityId);
        if (!string.IsNullOrWhiteSpace(request.Action))
            query = query.Where(a => a.ActionCode.Contains(request.Action));
        if (request.ActorUserId is { } uid)
            query = query.Where(a => a.ActorUserId == uid);

        var total = await query.CountAsync(ct);

        // Actor names resolved in bulk; deleted users fall back to 'user:{id}'.
        var rows = await query
            .OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.CreatedAtUtc,
                a.ActorUserId,
                a.ActionCode,
                a.EntityType,
                a.EntityId,
                a.OldValuesJson,
                a.NewValuesJson
            })
            .ToListAsync(ct);

        var userIds = rows.Where(r => r.ActorUserId is not null)
            .Select(r => r.ActorUserId!.Value).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        var items = rows.Select(r => new AuditLogRow(
            r.Id,
            r.CreatedAtUtc,
            r.ActorUserId is null ? null : names.GetValueOrDefault(r.ActorUserId.Value, $"user:{r.ActorUserId}"),
            r.ActionCode,
            r.EntityType,
            r.EntityId,
            r.OldValuesJson,
            r.NewValuesJson)).ToList();

        return Result<PagedResult<AuditLogRow>>.Success(
            PagedResult<AuditLogRow>.Create(items, total, page, pageSize));
    }
}
