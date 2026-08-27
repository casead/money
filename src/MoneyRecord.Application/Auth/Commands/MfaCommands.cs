using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Application.Auth.Commands;

/// <summary>SEC — step 1: generate a pending TOTP secret and return the otpauth URI.</summary>
public sealed record StartMfaEnrollmentCommand : IRequest<Result<MfaEnrollmentResponse>>, ICommand;

public sealed record MfaEnrollmentResponse(string Secret, string OtpAuthUri);

public sealed class StartMfaEnrollmentCommandHandler
    : IRequestHandler<StartMfaEnrollmentCommand, Result<MfaEnrollmentResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITotpService _totp;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public StartMfaEnrollmentCommandHandler(IMoneyRecordDbContext db, ICurrentUser currentUser,
        ITotpService totp, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _totp = totp;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<MfaEnrollmentResponse>> Handle(
        StartMfaEnrollmentCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result<MfaEnrollmentResponse>.Failure(ErrorCodes.Unauthorized, "Login ဝင်ပါ။");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null || !user.IsActive)
            return Result<MfaEnrollmentResponse>.Failure(ErrorCodes.Forbidden,
                "Account ရပ်တန့်ထားပါသည်။");

        if (user.MfaEnabled)
            return Result<MfaEnrollmentResponse>.Failure(ErrorCodes.ConflictState,
                "MFA သည် ဖွင့်ထားပြီးသားဖြစ်သည် — အရင်ပိတ်ပါ။");

        var secret = _totp.GenerateSecret();
        user.StartMfaEnrollment(secret, userId, _clock);

        await _audit.LogAsync("AUTH.MFA_ENROLL_START", "User", userId.ToString(), ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<MfaEnrollmentResponse>.Success(new MfaEnrollmentResponse(
            secret,
            _totp.BuildOtpAuthUri(secret, user.Username, "MoneyRecord")));
    }
}

/// <summary>SEC — step 2: verify a live code against the pending secret and activate MFA.</summary>
public sealed record ConfirmMfaEnrollmentCommand(string Code) : IRequest<Result<Unit>>, ICommand;

public sealed class ConfirmMfaEnrollmentCommandHandler
    : IRequestHandler<ConfirmMfaEnrollmentCommand, Result<Unit>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITotpService _totp;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public ConfirmMfaEnrollmentCommandHandler(IMoneyRecordDbContext db, ICurrentUser currentUser,
        ITotpService totp, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _totp = totp;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<Unit>> Handle(ConfirmMfaEnrollmentCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result<Unit>.Failure(ErrorCodes.Unauthorized, "Login ဝင်ပါ။");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null || !user.IsActive)
            return Result<Unit>.Failure(ErrorCodes.Forbidden, "Account ရပ်တန့်ထားပါသည်။");

        if (user.MfaEnabled)
            return Result<Unit>.Failure(ErrorCodes.ConflictState, "MFA ဖွင့်ထားပြီးသား။");

        if (user.MfaPendingSecret is null)
            return Result<Unit>.Failure(ErrorCodes.ValidationFailed,
                "MFA enrollment စတင်မထားပါ — /auth/mfa/enroll ကို အရင်ခေါ်ပါ။");

        if (!_totp.Validate(user.MfaPendingSecret, request.Code))
            return Result<Unit>.Failure(ErrorCodes.Unauthorized, "Code မှားနေပါသည်။");

        user.ConfirmMfaEnrollment(userId, _clock);
        await _audit.LogAsync("AUTH.MFA_ENABLED", "User", userId.ToString(), ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}

/// <summary>SEC — turn MFA off; requires a currently valid code.</summary>
public sealed record DisableMfaCommand(string Code) : IRequest<Result<Unit>>, ICommand;

public sealed class DisableMfaCommandHandler : IRequestHandler<DisableMfaCommand, Result<Unit>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITotpService _totp;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public DisableMfaCommandHandler(IMoneyRecordDbContext db, ICurrentUser currentUser,
        ITotpService totp, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _totp = totp;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<Unit>> Handle(DisableMfaCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result<Unit>.Failure(ErrorCodes.Unauthorized, "Login ဝင်ပါ။");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null || !user.IsActive)
            return Result<Unit>.Failure(ErrorCodes.Forbidden, "Account ရပ်တန့်ထားပါသည်။");

        if (!user.MfaEnabled)
            return Result<Unit>.Failure(ErrorCodes.ConflictState, "MFA ဖွင့်ထားခြင်း မရှိပါ။");

        if (!_totp.Validate(user.MfaSecret ?? string.Empty, request.Code))
            return Result<Unit>.Failure(ErrorCodes.Unauthorized, "Code မှားနေပါသည်။");

        user.DisableMfa(userId, _clock);
        await _audit.LogAsync("AUTH.MFA_DISABLED", "User", userId.ToString(), ct: ct);
        await _db.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}
