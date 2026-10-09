using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QuestPdfPrinterApi.Services;

/// <summary>
/// Holds the label template's physical page size. Every generated PDF is built at this size
/// (see ShippingLabelPdfService.TemplatePageSize); no endpoint lets a caller choose another.
/// </summary>
public static class PageSizeResolver
{
    private const float MmToPoints = 2.83465f;

    /// <summary>
    /// The label template's physical size - ISO C7 (81mm x 114mm nominal, i.e. 114mm x
    /// 81mm generated landscape to match the reference shipping label design). C7 is the
    /// closest standard envelope size to the label stock this template was originally tuned
    /// for (110mm x 84mm), and printers/label-stock vendors are far more likely to recognize
    /// "C7" than an arbitrary custom size.
    /// </summary>
    public static PageSize BuildDefault() => new(114f * MmToPoints, 81f * MmToPoints);
}
