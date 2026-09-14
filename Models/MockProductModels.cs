namespace QuestPdfPrinterApi.Models;

// Shape returned by GET /api/mock/products - a self-hosted stand-in "external" API endpoint.
// pages controls how many objects come back: pages=1 -> a 1-item array, pages=10 -> a
// 10-item array, and so on.

public class MockProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Body for POST /api/mock/products/preview. No userId - pages controls how many mock
/// products are generated (same as GET /api/mock/products?pages=N). PageSize accepts any
/// name from QuestPDF.Helpers.PageSizes (A4, A5, A3, Letter, Legal, Ledger, ...),
/// case-insensitive. Defaults to A4 when omitted.
/// </summary>
public record MockProductsPreviewRequest(int Pages = 1, string? PageSize = null, int? CompanionPort = null);

/// <summary>
/// Body for POST /api/mock/products/print. Same Pages/PageSize contract as
/// MockProductsPreviewRequest. PrinterName is required - use GET /api/printers to see
/// what's available.
/// </summary>
public record MockProductsPrintRequest(string PrinterName, int Pages = 1, string? PageSize = null);
