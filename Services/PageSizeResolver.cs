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
/// ORIENTATION: a named size like "A4" has no inherent orientation - QuestPDF's constants
/// are just "the two dimensions", conventionally portrait (narrower first). Callers pass a
/// PageOrientation alongside the name/default to say which dimension should be the width;
/// TryResolve swaps width/height when the resolved size's natural shape doesn't already
/// match what was asked for. Passing null defaults to whatever the size naturally is
/// (no swap) - see BuildDefault's own doc comment for the one exception (this API's default
/// 110mm x 84mm label, which is naturally landscape).
/// </summary>
public static class PageSizeResolver
{
    private const BindingFlags Lookup = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;

    private const float MmToPoints = 2.83465f;

    /// <summary>
    /// This API's default physical label size - 110mm x 84mm, generated landscape (matches
    /// the reference shipping label design). Used whenever a caller doesn't pass a named
    /// pageSize.
    /// </summary>
    public static PageSize BuildDefault() => new(110f * MmToPoints, 84f * MmToPoints);

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
    /// named sizes, landscape for this API's own 110mm x 84mm default).
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
