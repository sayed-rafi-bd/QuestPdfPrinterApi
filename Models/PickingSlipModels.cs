namespace QuestPdfPrinterApi.Models;

// Shape returned by GET /api/mock/picking-slips - a self-hosted stand-in "external" API
// endpoint. count controls how many objects come back: count=1 -> a 1-item array,
// count=10 -> a 10-item array, and so on. Each slip renders as exactly one page (see
// PickingSlipPdfService), so count == page count here too.

public class PickingSlipItem
{
    /// <summary>JANコード - JAN/EAN product barcode.</summary>
    public string JanCode { get; set; } = "";

    /// <summary>商品名 - product name.</summary>
    public string ProductName { get; set; } = "";

    /// <summary>数量 - quantity.</summary>
    public int Quantity { get; set; }
}

public class PickingSlip
{
    /// <summary>Picking number, e.g. "877250700-1" (PicNo.).</summary>
    public string PicNo { get; set; } = "";

    /// <summary>検品日 - the inspection date printed on the slip.</summary>
    public DateTime InspectionDate { get; set; }

    /// <summary>Whether to print the "納品書在中" (delivery note enclosed) flag.</summary>
    public bool DeliveryNoteEnclosed { get; set; } = true;

    /// <summary>1-based page number for the "N/M" counter in the header.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>Total page count for the "N/M" counter in the header.</summary>
    public int PageCount { get; set; } = 1;

    /// <summary>Warehouse/region code, e.g. "Aichi".</summary>
    public string WarehouseCode { get; set; } = "";

    /// <summary>Client/system code, e.g. "FMH29".</summary>
    public string ClientCode { get; set; } = "";

    /// <summary>
    /// Customer/recipient name. Printed verbatim - this field is known to carry
    /// arbitrary punctuation (quotes, angle brackets, ampersands, full-width forms) in
    /// real data, so nothing here should assume it's "clean" text. QuestPDF's Text()
    /// element draws it as literal characters (there's no markup parsing to worry
    /// about), so no HTML-style escaping is needed.
    /// </summary>
    public string CustomerName { get; set; } = "";

    /// <summary>
    /// Carrier code, e.g. "054". Also the payload encoded in the barcode and printed as
    /// its human-readable "*054*" caption.
    /// </summary>
    public string CarrierCode { get; set; } = "";

    /// <summary>Carrier display name, e.g. "ヤマト運輸(LA)".</summary>
    public string CarrierName { get; set; } = "";

    public List<PickingSlipItem> Items { get; set; } = new();

    /// <summary>アイテム数 - number of distinct line items (not total units).</summary>
    public int ItemCount => Items.Count;

    /// <summary>合計数 - total units across all line items.</summary>
    public int TotalQuantity => Items.Sum(i => i.Quantity);
}

/// <summary>
/// Body for POST /api/mock/picking-slips/preview. count=1 -> 1 slip, count=10 -> 10
/// slips, and so on.
/// </summary>
public record PickingSlipsPreviewRequest(int Count = 1, int? CompanionPort = null, string? PageSize = null);

/// <summary>
/// Body for POST /api/mock/picking-slips/print. Same Count contract as
/// PickingSlipsPreviewRequest. PrinterName is required - use GET /api/printers to see
/// what's available. Dpi is an optional per-job override (see PrinterService); omit it
/// to print at the printer's own current default resolution, which is the right choice
/// for most Bluetooth/thermal label printers since they usually only support one or two
/// fixed native resolutions (commonly 203, 300, or 600 dpi) rather than an arbitrary value.
/// </summary>
public record PickingSlipsPrintRequest(string PrinterName, int Count = 1, string? PageSize = null, int? Dpi = null);
