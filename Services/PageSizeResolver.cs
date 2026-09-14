using System.Reflection;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QuestPdfPrinterApi.Services;

/// <summary>
/// Maps a page size name from a request body/query string (e.g. "A4", "Letter", "Legal")
/// to one of QuestPDF's QuestPDF.Helpers.PageSizes constants. Uses reflection instead of an
/// exhaustive switch so every size QuestPDF ships (A0-A10, B-series, Letter, Legal, Ledger,
/// Executive, ...) is supported automatically, case-insensitively.
/// </summary>
public static class PageSizeResolver
{
    private const BindingFlags Lookup = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;

    public static bool TryResolve(string? name, out PageSize pageSize, out string? error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            pageSize = PageSizes.A4;
            error = null;
            return true;
        }

        var type = typeof(PageSizes);
        var value = type.GetProperty(name, Lookup)?.GetValue(null)
                 ?? type.GetField(name, Lookup)?.GetValue(null);

        if (value is PageSize resolved)
        {
            pageSize = resolved;
            error = null;
            return true;
        }

        pageSize = PageSizes.A4;
        error = $"Unknown pageSize '{name}'. Examples: A4, A5, A3, Letter, Legal, Ledger.";
        return false;
    }
}
