using Syntera.Application.DTOs.Sales;
using FluentValidation;

namespace Syntera.Application.Validators;

public sealed class SaleCreateValidator : AbstractValidator<SaleCreateDto>
{
    public SaleCreateValidator()
    {
        RuleFor(x => x.CustomerId).NotEqual(Guid.Empty).WithMessage("Pelanggan wajib dipilih.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Penjualan harus memiliki minimal 1 item.");
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 100);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEqual(Guid.Empty);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPrice).GreaterThan(0);
            item.RuleFor(i => i.DiscountAmount).GreaterThanOrEqualTo(0);
        });
    }
}
