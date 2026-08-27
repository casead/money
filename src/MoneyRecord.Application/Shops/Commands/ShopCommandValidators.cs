using FluentValidation;

namespace MoneyRecord.Application.Shops.Commands;

/// <summary>TEN-001…003 field rules.</summary>
public sealed class CreateShopCommandValidator : AbstractValidator<CreateShopCommand>
{
    public CreateShopCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Shop Code လိုအပ်ပါသည်။")
            .Length(2, 20).WithMessage("Code သည် 2–20 လုံး ရှိရမည်။")
            .Matches("^[A-Za-z0-9_-]+$")
                .WithMessage("Code တွင် a-z, A-Z, 0-9, _, - သာ အသုံးပြုနိုင်ပါသည်။");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("ဆိုင်နာမည် လိုအပ်ပါသည်။")
            .Length(2, 100).WithMessage("ဆိုင်နာမည်သည် 2–100 လုံး ရှိရမည်။");
    }
}

public sealed class UpdateShopCommandValidator : AbstractValidator<UpdateShopCommand>
{
    public UpdateShopCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("ဆိုင်နာမည် လိုအပ်ပါသည်။")
            .Length(2, 100).WithMessage("ဆိုင်နာမည်သည် 2–100 လုံး ရှိရမည်။");
    }
}

public sealed class SetShopStatusCommandValidator : AbstractValidator<SetShopStatusCommand>
{
    public SetShopStatusCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}
