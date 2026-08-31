using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Balances.Commands;

// ---------- PRV-002 Create ----------

public sealed record CreateProviderCommand(
    string Code,
    string Name,
    string? LogoUrl,
    int? DisplayOrder) : IRequest<Result<ProviderResponse>>, ICommand;

public sealed record ProviderResponse(
    int Id,
    string Code,
    string Name,
    string? LogoUrl,
    int DisplayOrder,
    bool IsActive,
    int Accounts,
    long TotalFloat);

public sealed class CreateProviderCommandValidator : AbstractValidator<CreateProviderCommand>
{
    public CreateProviderCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().Length(2, 20)
            .Matches("^[A-Z0-9_]+$")
                .WithMessage("Code သည် A-Z, 0-9, _ (uppercase) သာ ဖြစ်ရမည်။");
        RuleFor(x => x.Name).Length(2, 50);
    }
}

public sealed class CreateProviderCommandHandler
    : IRequestHandler<CreateProviderCommand, Result<ProviderResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IAuditLogger _audit;

    public CreateProviderCommandHandler(IMoneyRecordDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<ProviderResponse>> Handle(CreateProviderCommand request,
        CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.WalletProviders.AnyAsync(p => p.Code == code, ct))
            return Result<ProviderResponse>.Failure(
                ErrorCodes.Duplicate, $"Provider code '{code}' ရှိပြီးသား ဖြစ်နေပါသည်။");

        var provider = new WalletProvider(code, request.Name, request.LogoUrl,
            request.DisplayOrder ?? 0);
        _db.WalletProviders.Add(provider);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("PROVIDER.CREATE", "WalletProvider", provider.Id.ToString(),
            newValue: System.Text.Json.JsonSerializer.Serialize(new { provider.Id, code }),
            ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<ProviderResponse>.Success(new ProviderResponse(
            provider.Id, provider.Code, provider.Name, provider.LogoUrl,
            provider.DisplayOrder, provider.IsActive, 0, 0));
    }
}

// ---------- PRV-003 Update ----------

public sealed record UpdateProviderCommand(
    int Id,
    string? Code,
    string? Name,
    string? LogoUrl,
    int? DisplayOrder) : IRequest<Result<ProviderResponse>>, ICommand;

public sealed class UpdateProviderCommandHandler
    : IRequestHandler<UpdateProviderCommand, Result<ProviderResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IAuditLogger _audit;

    public UpdateProviderCommandHandler(IMoneyRecordDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<ProviderResponse>> Handle(UpdateProviderCommand request,
        CancellationToken ct)
    {
        var provider = await _db.WalletProviders
            .FirstOrDefaultAsync(p => p.Id == request.Id && !p.IsDeleted, ct);
        if (provider is null)
            return Result<ProviderResponse>.Failure(ErrorCodes.NotFound,
                "Provider ရှာမတွေ့ပါ။");

        // Check duplicate code if changing
        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            var newCode = request.Code.Trim().ToUpperInvariant();
            if (newCode != provider.Code &&
                await _db.WalletProviders.AnyAsync(p => p.Code == newCode && !p.IsDeleted, ct))
                return Result<ProviderResponse>.Failure(ErrorCodes.Duplicate,
                    $"Provider code '{newCode}' ရှိပြီးသား ဖြစ်နေပါသည်။");
        }

        var before = System.Text.Json.JsonSerializer.Serialize(
            new { provider.Code, provider.Name, provider.LogoUrl, provider.DisplayOrder });
        provider.Update(request.Code, request.Name, request.LogoUrl, request.DisplayOrder);
        var after = System.Text.Json.JsonSerializer.Serialize(
            new { provider.Code, provider.Name, provider.LogoUrl, provider.DisplayOrder });

        await _audit.LogAsync("PROVIDER.UPDATE", "WalletProvider",
            provider.Id.ToString(), before, after, ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<ProviderResponse>.Success(await ProjectAsync(_db, provider, ct));
    }

    internal static async Task<ProviderResponse> ProjectAsync(
        IMoneyRecordDbContext db, WalletProvider p, CancellationToken ct)
    {
        var accounts = await db.WalletAccounts.IgnoreQueryFilters()
            .Where(a => a.WalletProviderId == p.Id && !a.IsDeleted)
            .ToListAsync(ct);
        var count = accounts.Count;
        var totalFloat = accounts.Sum(a => a.CurrentFloatBalance);
        return new ProviderResponse(p.Id, p.Code, p.Name, p.LogoUrl,
            p.DisplayOrder, p.IsActive, count, totalFloat);
    }
}

// ---------- PRV-004 Activate/Deactivate ----------

public sealed record SetProviderStatusCommand(int Id, bool IsActive)
    : IRequest<Result<ProviderResponse>>, ICommand;

public sealed class SetProviderStatusCommandHandler
    : IRequestHandler<SetProviderStatusCommand, Result<ProviderResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IAuditLogger _audit;

    public SetProviderStatusCommandHandler(IMoneyRecordDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<ProviderResponse>> Handle(SetProviderStatusCommand request,
        CancellationToken ct)
    {
        var provider = await _db.WalletProviders
            .FirstOrDefaultAsync(p => p.Id == request.Id && !p.IsDeleted, ct);
        if (provider is null)
            return Result<ProviderResponse>.Failure(ErrorCodes.NotFound,
                "Provider ရှာမတွေ့ပါ။");

        if (provider.IsActive != request.IsActive)
        {
            provider.SetActive(request.IsActive);
            // Deactivation blocks NEW txns on its accounts (BR enforcement lands in M6 engine).
            await _audit.LogAsync("PROVIDER.STATUS_CHANGE", "WalletProvider",
                provider.Id.ToString(),
                oldValue: System.Text.Json.JsonSerializer.Serialize(new { IsActive = !request.IsActive }),
                newValue: System.Text.Json.JsonSerializer.Serialize(new { IsActive = request.IsActive }),
                ct: ct);
            await _db.SaveChangesAsync(ct);
        }

        return Result<ProviderResponse>.Success(
            await UpdateProviderCommandHandler.ProjectAsync(_db, provider, ct));
    }
}

// ---------- PRV-005 Delete (soft-delete, Admin-only) ----------

public sealed record DeleteProviderCommand(int Id)
    : IRequest<Result>, ICommand;

public sealed class DeleteProviderCommandValidator : AbstractValidator<DeleteProviderCommand>
{
    public DeleteProviderCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public sealed class DeleteProviderCommandHandler
    : IRequestHandler<DeleteProviderCommand, Result>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly ICurrentUser _currentUser;

    public DeleteProviderCommandHandler(IMoneyRecordDbContext db, IAuditLogger audit,
        ICurrentUser currentUser)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeleteProviderCommand request, CancellationToken ct)
    {
        var provider = await _db.WalletProviders
            .FirstOrDefaultAsync(p => p.Id == request.Id && !p.IsDeleted, ct);
        if (provider is null)
            return Result.Failure(ErrorCodes.NotFound, "Provider ရှာမတွေ့ပါ။");

        // Block delete if provider has active wallet accounts.
        var hasAccounts = await _db.WalletAccounts
            .AnyAsync(a => a.WalletProviderId == provider.Id && !a.IsDeleted, ct);
        if (hasAccounts)
            return Result.Failure(ErrorCodes.ProviderHasAccounts,
                "ဤ provider တွင် wallet account များ ရှိနေသေးသည်။ အရင်ဖျက်ပါ။");

        provider.Delete(_currentUser.UserId ?? 0);
        await _audit.LogAsync("PROVIDER.DELETE", "WalletProvider",
            provider.Id.ToString(),
            newValue: System.Text.Json.JsonSerializer.Serialize(new { provider.Id, provider.Code }),
            ct: ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
