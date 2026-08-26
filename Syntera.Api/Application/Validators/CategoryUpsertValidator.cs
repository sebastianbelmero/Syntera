using Syntera.Application.DTOs.Categories;
using FluentValidation;

namespace Syntera.Application.Validators;

public sealed class CategoryUpsertValidator : AbstractValidator<CategoryUpsertDto>
{
    public CategoryUpsertValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Nama kategori wajib diisi.")
            .MaximumLength(120).WithMessage("Nama kategori maksimal 120 karakter.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
