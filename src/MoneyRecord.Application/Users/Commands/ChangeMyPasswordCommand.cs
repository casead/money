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
/// Self-service password change (AUTH-005). Requires the current password,
/// then rotates the hash and revokes ALL sessions (re-login everywhere).
/// </summary>
public sealed record ChangeMyPasswordCommand(string CurrentPassword, string NewPassword)
    : IRequest<Result<Unit>>, ICommand;

public sealed class ChangeMyPasswordCommandValidator : AbstractValidator<ChangeMyPasswordCommand>
{
    public ChangeMyPasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("လက်ရှိ Password ထည့်ပါ။");
        RuleFor(x => x.NewPassword).ApplyPasswordPolicy();
    }
}

public sealed class ChangeMyPasswordCommandHandler
    : IRequestHandler<ChangeMyPasswordCommand, Result<Unit>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public ChangeMyPasswordCommandHandler(IMoneyRecordDbContext db, IPasswordHasher hasher,
        IClock clock, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<Unit>> Handle(ChangeMyPasswordCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result<Unit>.Failure(ErrorCodes.Unauthorized, "Login ဝင်ပါ။");

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null || !user.IsActive)
            return Result<Unit>.Failure(ErrorCodes.Forbidden, "Account ရပ်တန့်ထားပါသည်။");

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result<Unit>.Failure(ErrorCodes.Unauthorized,
                "လက်ရှိ Password မှားနေပါသည်။");

        user.SetPassword(_hasher.Hash(request.NewPassword), userId, _clock);

        // Force re-login on every device after a credential change.
        await RevokeAllSessionsAsync(user.Id, ct);

        await _audit.LogAsync("AUTH.PASSWORD_CHANGE", "User", user.Id.ToString(), ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }

    private async Task RevokeAllSessionsAsync(long uid, CancellationToken ct)
    {
        var active = await _db.RefreshTokens
            .Where(rt => rt.UserId == uid && rt.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in active)
            token.Revoke(_clock.UtcNow);
    }
}
