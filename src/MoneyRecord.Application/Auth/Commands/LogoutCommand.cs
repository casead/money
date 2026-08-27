using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Entities;
using FluentValidation;

namespace MoneyRecord.Application.Auth.Commands;

/// <summary>AUTH-002 — logout: revoke the presented RT only; idempotent (repeat = 204).</summary>
public sealed record LogoutCommand(string RefreshToken) : IRequest<Result<Unit>>, ICommand;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("RefreshToken ထည့်ပါ။");
    }
}

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result<Unit>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public LogoutCommandHandler(IMoneyRecordDbContext db, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<Unit>> Handle(LogoutCommand request, CancellationToken ct)
    {
        var tokenHash = ITokenService.HashRefreshToken(request.RefreshToken);
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (stored is not null && stored.RevokedAtUtc is null)
        {
            stored.Revoke(_clock.UtcNow);
            await _audit.LogAsync("AUTH.LOGOUT", "User", stored.UserId.ToString(), ct: ct);
            await _db.SaveChangesAsync(ct);
        }
        // Unknown or already-revoked token → still success (idempotent per API-007 AUTH-002).

        return Result<Unit>.Success(Unit.Value);
    }
}
