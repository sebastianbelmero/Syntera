using Syntera.Application.DTOs.Suppliers;
using FluentValidation;

namespace Syntera.Application.Validators;

public sealed class SupplierUpsertValidator : AbstractValidator<SupplierUpsertDto>
{
    public SupplierUpsertValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Matches(@"^[0-9+\-\s()]{6,24}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Nomor telepon tidak valid.");
    }
}
