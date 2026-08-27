using MediatR;
using MoneyRecord.Application.Common.Behaviors;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;

namespace MoneyRecord.Application.Auth.Commands;

public sealed record LoginCommand(
    string Username,
    string Password,
    string? DeviceInfo,
    string? TotpCode = null) : IRequest<Result<LoginResponse>>, ICommand;

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSec,
    CurrentUserDto User);

public sealed record CurrentUserDto(
    long Id,
    string Username,
    string FullName,
    string RoleCode,
    long? ShopId = null);
