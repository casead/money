using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Users.Commands;

/// <summary>
/// USR-006 — Admin resets a user's password (user.manage). No current-password check
/// (that is the caller's own flow); target's sessions are revoked (forced re-login).
/// </summary>
public sealed record ResetUserPasswordCommand(long UserId, string NewPassword)
    : IRequest<Result<Unit>>, ICommand;

public sealed class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.NewPassword).ApplyPasswordPolicy();
    }
}

public sealed class ResetUserPasswordCommandHandler
    : IRequestHandler<ResetUserPasswordCommand, Result<Unit>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public ResetUserPasswordCommandHandler(IMoneyRecordDbContext db, IPasswordHasher hasher,
        IClock clock, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<Unit>> Handle(ResetUserPasswordCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? 0;

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && !u.IsDeleted, ct);
        if (user is null)
            return Result<Unit>.Failure(ErrorCodes.NotFound, "User ရှာမတွေ့ပါ။");

        user.SetPassword(_hasher.Hash(request.NewPassword), actorId, _clock);

        // Target must re-login on every device with the new credential.
        var now = _clock.UtcNow;
        var active = await _db.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in active)
            token.Revoke(now);

        await _audit.LogAsync("USER.PASSWORD_RESET", "User", user.Id.ToString(),
            newValue: $"actor={actorId}", ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}
