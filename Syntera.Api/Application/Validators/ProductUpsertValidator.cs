using Syntera.Application.DTOs.Products;
using FluentValidation;

namespace Syntera.Application.Validators;

public sealed class ProductUpsertValidator : AbstractValidator<ProductUpsertDto>
{
    public ProductUpsertValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.CategoryId).NotEqual(Guid.Empty).WithMessage("Kategori wajib dipilih.");
        RuleFor(x => x.SupplierId).NotEqual(Guid.Empty).WithMessage("Pemasok wajib dipilih.");
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0).WithMessage("Harga pokok ≥ 0.");
        RuleFor(x => x.SellingPrice).GreaterThan(0).WithMessage("Harga jual > 0.");
        RuleFor(x => x.SellingPrice)
            .GreaterThan(x => x.CostPrice)
            .When(x => x.CostPrice > 0, ApplyConditionTo.CurrentValidator)
            .WithMessage("Harga jual harus lebih besar dari harga pokok.");
        RuleFor(x => x.ReorderLevel).InclusiveBetween(0, 1_000_000);
        RuleFor(x => x.ExpiryDate)
            .GreaterThan(DateTime.UtcNow)
            .When(x => x.ExpiryDate.HasValue)
            .WithMessage("Tanggal kadaluarsa harus di masa depan.");
    }
}

public sealed class ProductStockAdjustValidator : AbstractValidator<ProductStockAdjustDto>
{
    public ProductStockAdjustValidator()
    {
        RuleFor(x => x.Quantity).NotEqual(0).WithMessage("Quantity tidak boleh 0.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
