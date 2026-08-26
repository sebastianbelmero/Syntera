using Syntera.Application.DTOs.Customers;
using FluentValidation;

namespace Syntera.Application.Validators;

public sealed class CustomerUpsertValidator : AbstractValidator<CustomerUpsertDto>
{
    public CustomerUpsertValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
