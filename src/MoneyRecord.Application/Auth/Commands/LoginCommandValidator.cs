using FluentValidation;

namespace MoneyRecord.Application.Auth.Commands;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username ထည့်ပါ။")
            .Length(3, 50).WithMessage("Username သည် စာလုံးရေ ၃ မှ ၅၀ ကြားရမည်။");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password ထည့်ပါ။")
            .Length(8, 64).WithMessage("Password သည် စာလုံးရေ ၈ မှ ၆၄ ကြားရမည်။");
    }
}
