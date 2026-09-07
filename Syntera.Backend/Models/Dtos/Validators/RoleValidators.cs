using FluentValidation;
using Syntera.Backend.Models.Dtos.Roles;

namespace Syntera.Backend.Models.Dtos.Validators;

// ── COMPLIANCE (Sprint 2.7): Two-person approval request validators ────
// These validators run automatically as part of the model-binding pipeline
// (registered via AddValidatorsFromAssemblyContaining<LoginRequestValidator>()
// in ServiceCollectionExtensions). A failed validation surfaces as a 400
// with errorCode="VALIDATION_FAILED" and the standard ApiResponse envelope
// (see InvalidModelStateResponseFactory in ServiceCollectionExtensions).

/// <summary>
/// Validator for the approve-publish request body
/// (POST /api/platform/role-templates/{id}/approve). SECURITY (L1):
/// <list type="bullet">
///   <item>SignatureMeaning: required, ≤ 500 chars. 21 CFR Part 11 §11.50
///     requires the meaning of an electronic signature to be recorded at
///     the time of signing — so we cannot let it be blank. The 500-char
///     cap matches the ApproverSignatureMeaning column width in the
///     RoleTemplateApprovals table and the SignatureMeaning column in
///     AuditLogs.</item>
/// </list>
/// </summary>
public sealed class ApprovePublishRequestValidator : AbstractValidator<ApprovePublishRequest>
{
    public ApprovePublishRequestValidator()
    {
        RuleFor(x => x.SignatureMeaning)
            .NotEmpty().WithMessage("SignatureMeaning is required for approval (21 CFR Part 11 §11.50).")
            .MaximumLength(500).WithMessage("SignatureMeaning must not exceed 500 characters.");
    }
}

/// <summary>
/// Validator for the reject-publish request body
/// (POST /api/platform/role-templates/{id}/reject). SECURITY (L1):
/// <list type="bullet">
///   <item>Reason: required, ≤ 2000 chars. The rejection reason is what
///     the requester (and the auditor) sees in the audit trail — without
///     it, a rejected publish is indistinguishable from a system error.
///     The 2000-char cap matches the RejectionReason column width in the
///     RoleTemplateApprovals table.</item>
/// </list>
/// </summary>
public sealed class RejectPublishRequestValidator : AbstractValidator<RejectPublishRequest>
{
    public RejectPublishRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required for rejection.")
            .MaximumLength(2000).WithMessage("Reason must not exceed 2000 characters.");
    }
}
