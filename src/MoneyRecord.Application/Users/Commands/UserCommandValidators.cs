using FluentValidation;
using MoneyRecord.Domain.Common.Rbac;

namespace MoneyRecord.Application.Users.Commands;

/// <summary>Shared S-A02 password policy for create/change/reset flows.</summary>
public static class PasswordRuleExtensions
{
    public static IRuleBuilderOptions<T, string> ApplyPasswordPolicy<T>(
        this IRuleBuilder<T, string> rule) => rule
            .NotEmpty().WithMessage("Password လိုအပ်ပါသည်။")
            .MinimumLength(CreateUserCommandValidator.MinPasswordLength)
                .WithMessage($"Password သည် အနည်းဆုံး {CreateUserCommandValidator.MinPasswordLength} လုံး ရှိရမည်။")
            .MaximumLength(CreateUserCommandValidator.MaxPasswordLength)
            .Matches("[A-Za-z]").WithMessage("Password တွင် စာလုံး တစ်လုံး ပါဝင်ရမည်။")
            .Matches(@"\d").WithMessage("Password တွင် ဂဏန်း တစ်လုံး ပါဝင်ရမည်။");
}

/// <summary>USR-002 field rules (FR-006, S-A02 password policy).</summary>
public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username လိုအပ်ပါသည်။")
            .Length(3, 50).WithMessage("Username သည် 3–50 လုံး ရှိရမည်။")
            .Matches("^[a-zA-Z0-9_]+$")
                .WithMessage("Username တွင် a-z, A-Z, 0-9, _ သာ အသုံးပြုနိုင်ပါသည်။");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password လိုအပ်ပါသည်။")
            .MinimumLength(MinPasswordLength)
                .WithMessage($"Password သည် အနည်းဆုံး {MinPasswordLength} လုံး ရှိရမည်။")
            .MaximumLength(MaxPasswordLength)
            .Matches("[A-Za-z]").WithMessage("Password တွင် စာလုံး တစ်လုံး ပါဝင်ရမည်။")
            .Matches(@"\d").WithMessage("Password တွင် ဂဏန်း တစ်လုံး ပါဝင်ရမည်။");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("FullName လိုအပ်ပါသည်။")
            .Length(2, 100).WithMessage("FullName သည် 2–100 လုံး ရှိရမည်။");

        RuleFor(x => x.Phone)
            .Matches("^\\d{9,15}$")
                .When(x => !string.IsNullOrWhiteSpace(x.Phone))
                .WithMessage("Phone သည် ဂဏန်းသက်သက် 9–15 လုံး ဖြစ်ရမည်။");

        RuleFor(x => x.RoleId)
            .Must(id => id is RolePermissionRegistry.AdminRoleId or RolePermissionRegistry.StaffRoleId)
                .WithMessage("Role သည် Admin (2) သို့မဟုတ် Staff (3) သာ ဖြစ်ရမည်။");
    }
}

/// <summary>USR-004 shares USR-002 field rules for the optional fields.</summary>
public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);

        RuleFor(x => x.FullName)
            .Length(2, 100).WithMessage("FullName သည် 2–100 လုံး ရှိရမည်။")
            .When(x => x.FullName is not null);

        RuleFor(x => x.Phone)
            .Matches("^\\d{9,15}$")
                .When(x => !string.IsNullOrWhiteSpace(x.Phone))
                .WithMessage("Phone သည် ဂဏန်းသက်သက် 9–15 လုံး ဖြစ်ရမည်။");

        RuleFor(x => x.RoleId)
            .Must(id => id is null or RolePermissionRegistry.AdminRoleId or RolePermissionRegistry.StaffRoleId)
                .WithMessage("Role သည် Admin (2) သို့မဟုတ် Staff (3) သာ ဖြစ်ရမည်။");
    }
}

/// <summary>USR-005 body: { isActive }.</summary>
public sealed class SetUserStatusCommandValidator : AbstractValidator<SetUserStatusCommand>
{
    public SetUserStatusCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}
