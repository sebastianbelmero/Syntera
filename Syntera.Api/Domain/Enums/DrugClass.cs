namespace Syntera.Domain.Enums;

/// <summary>
/// Drug classification as defined by Indonesian Ministry of Health
/// (Kemenkes) and regulated under PMK No. 73/2016.
/// Used to drive business rules (e.g. prescription requirement).
/// </summary>
public enum DrugClass
{
    /// <summary>Obat Bebas — sold without prescription (green circle).</summary>
    OverTheCounter = 1,

    /// <summary>Obat Bebas Terbatas — limited OTC (blue circle).</summary>
    RestrictedOTC = 2,

    /// <summary>Obat Keras — prescription required (red circle with K).</summary>
    PrescriptionOnly = 3,

    /// <summary>Obat Wajib Apotek — pharmacy-only, no prescription.</summary>
    PharmacyOnly = 4,

    /// <summary>Narcotic / Psychotropic — BPOM special licence required.</summary>
    Narcotic = 5,
}
