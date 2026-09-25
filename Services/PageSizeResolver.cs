using System.Reflection;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;

namespace QuestPdfPrinterApi.Services;

/// <summary>
/// Maps a page size name from a request body/query string (e.g. "A4", "Letter", "Legal")
/// to one of QuestPDF's QuestPDF.Helpers.PageSizes constants. Uses reflection instead of an
/// exhaustive switch so every size QuestPDF ships (A0-A10, B-series, Letter, Legal, Ledger,
/// Executive, ...) is supported automatically, case-insensitively.
///
/// Used by /pdf and /preview only, to size the PDF QuestPDF actually generates. /print does
/// NOT use this - its pageSize is a free-form string forwarded straight to SumatraPDF's
/// "paper=" token instead (see ShippingLabelsPrintRequest's remarks), so /print isn't
/// limited to the named sizes this class knows about.
///
/// ORIENTATION: a named size like "A4" has no inherent orientation - QuestPDF's constants
/// are just "the two dimensions", conventionally portrait (narrower first). Callers pass a
/// PageOrientation alongside the name/default to say which dimension should be the width;
/// TryResolve swaps width/height when the resolved size's natural shape doesn't already
/// match what was asked for. Passing null defaults to whatever the size naturally is
/// (no swap) - see BuildDefault's own doc comment for the one exception (this API's default
/// ISO C7 label, which is generated landscape).
/// </summary>
public static class PageSizeResolver
{
    private const BindingFlags Lookup = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;

    private const float MmToPoints = 2.83465f;

    /// <summary>
    /// This API's default physical label size - ISO C7 (81mm x 114mm nominal, i.e. 114mm x
    /// 81mm generated landscape to match the reference shipping label design). C7 is the
    /// closest standard envelope size to the label stock this template was originally tuned
    /// for (110mm x 84mm), and printers/label-stock vendors are far more likely to recognize
    /// "C7" than an arbitrary custom size. Used whenever a caller doesn't pass a named
    /// pageSize.
    /// </summary>
    public static PageSize BuildDefault() => new(114f * MmToPoints, 81f * MmToPoints);

    public static bool TryResolve(string? name, PageOrientation? orientation, out PageSize pageSize, out string? error)
    {
        PageSize resolved;

        if (string.IsNullOrWhiteSpace(name))
        {
            resolved = BuildDefault();
        }
        else
        {
            var type = typeof(PageSizes);
            var value = type.GetProperty(name, Lookup)?.GetValue(null)
                     ?? type.GetField(name, Lookup)?.GetValue(null);

            if (value is not PageSize namedSize)
            {
                pageSize = BuildDefault();
                error = $"Unknown pageSize '{name}'. Examples: A4, A5, A3, Letter, Legal, Ledger.";
                return false;
            }

            resolved = namedSize;
        }

        pageSize = ApplyOrientation(resolved, orientation);
        error = null;
        return true;
    }

    /// <summary>
    /// Swaps width/height so the page matches the requested orientation. Null leaves the
    /// resolved size exactly as-is (whatever shape it naturally has - portrait for QuestPDF's
    /// named sizes, landscape for this API's own ISO C7 default).
    /// </summary>
    private static PageSize ApplyOrientation(PageSize size, PageOrientation? orientation)
    {
        if (orientation is null)
            return size;

        var isLandscape = size.Width > size.Height;
        var wantsLandscape = orientation == PageOrientation.Landscape;

        return isLandscape == wantsLandscape ? size : new PageSize(size.Height, size.Width);
    }
}
