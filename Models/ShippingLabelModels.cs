namespace QuestPdfPrinterApi.Models;

// Shape returned by GET /api/mock/shipping-labels - a self-hosted stand-in "external" API
// endpoint. count controls how many objects come back: count=1 -> a 1-item array,
// count=10 -> a 10-item array, and so on. Each label renders as exactly one page (see
// ShippingLabelPdfService), so count == page count here too.

public class ShippingLabel
{
    /// <summary>〒 postal code of the recipient, e.g. "150-0021".</summary>
    public string RecipientPostalCode { get; set; } = "";

    /// <summary>Recipient's full address, printed as a single line under the postal code.</summary>
    public string RecipientAddress { get; set; } = "";

    /// <summary>Large leading digits of the carrier sort/area code, e.g. "7013".</summary>
    public string SortCodeMain { get; set; } = "";

    /// <summary>Smaller trailing digits printed immediately after SortCodeMain, e.g. "342".</summary>
    public string SortCodeSuffix { get; set; } = "";

    /// <summary>Recipient's phone number as printed next to "TEL:".</summary>
    public string RecipientTel { get; set; } = "";

    /// <summary>Large carrier name printed top-right, e.g. "佐川".</summary>
    public string CarrierDisplayName { get; set; } = "";

    /// <summary>
    /// Recipient/addressee name. Printed verbatim with a trailing "様" appended by the
    /// template - like PickingSlip.CustomerName before it, this field is known to carry
    /// arbitrary punctuation in real data, so nothing here should assume it's "clean" text.
    /// </summary>
    public string RecipientName { get; set; } = "";

    /// <summary>
    /// お問合せNo. - the tracking number encoded (and printed as a spaced-out caption) in
    /// the Codabar barcode. Digits only; the template adds the leading/trailing "a"
    /// start/stop characters both to the barcode payload and to the printed caption.
    /// </summary>
    public string TrackingNumber { get; set; } = "";

    /// <summary>総個数 - total physical pieces in the shipment (the "N個口" count).</summary>
    public int TotalPieces { get; set; } = 1;

    /// <summary>運送会社 - carrier + service name, e.g. "佐川代引（LC）".</summary>
    public string CarrierServiceName { get; set; } = "";

    /// <summary>出荷日 - the date the shipment left the warehouse.</summary>
    public DateTime ShipDate { get; set; }

    /// <summary>お届指定日 - the requested/confirmed delivery date.</summary>
    public DateTime DeliveryDate { get; set; }

    /// <summary>時間帯指定 - requested delivery time window, e.g. "16時～18時".</summary>
    public string TimeSlot { get; set; } = "";

    /// <summary>Sender company/site name, e.g. "FMHテスト用(テスト環境00)".</summary>
    public string SenderName { get; set; } = "";

    /// <summary>Sender's 〒 postal code.</summary>
    public string SenderPostalCode { get; set; } = "";

    /// <summary>Sender's address, first line.</summary>
    public string SenderAddressLine1 { get; set; } = "";

    /// <summary>Sender's address, second line (building name etc.) - omit if not needed.</summary>
    public string SenderAddressLine2 { get; set; } = "";

    /// <summary>Sender's phone number as printed next to "TEL:".</summary>
    public string SenderTel { get; set; } = "";

    /// <summary>[備考] COD/collect-on-delivery amount in yen, e.g. 3960. Null hides the row.</summary>
    public int? CodAmountYen { get; set; }

    /// <summary>出荷依頼番号 - the shipping request number printed under the remarks amount.</summary>
    public string ShippingRequestNumber { get; set; } = "";
}

/// <summary>
/// Body for POST /api/mock/shipping-labels/preview. count=1 -> 1 label, count=10 -> 10
/// labels, and so on. PageSize controls the generated PDF's own page geometry - see
/// PageSizeResolver; defaults to this API's ISO C7 (114mm x 81mm) landscape label. No
/// Orientation here - preview always renders a named PageSize in its landscape form.
/// </summary>
public record ShippingLabelsPreviewRequest(
    int Count = 1,
    int? CompanionPort = null,
    string? PageSize = null);

/// <summary>
/// Body for POST /api/mock/shipping-labels/print. PrinterName is required - use GET
/// /api/printers to see what's available. Count controls how many labels/pages.
///
/// The PDF itself is always built at this API's default ISO C7 (114mm x 81mm) landscape
/// label size - PageSize/Orientation/FitMode here do NOT change that geometry. Instead
/// they're forwarded as-is to SumatraPDF's -print-settings for the physical print job (see
/// PrinterService.PrintFileAsync):
///  - PageSize becomes the "paper=" token - any value SumatraPDF/the driver accepts (a
///    name like "A4", or a custom size like "76mm x 130mm"). Omit it to print on whatever
///    paper the printer is currently configured for.
///  - Orientation becomes "portrait"/"landscape" (default Landscape).
///  - FitMode becomes "noscale" (Fit, the default) or "fit" (Contain).
///
/// Dpi is an optional per-job override (see PrinterService); omit it to print at the
/// printer's own current default resolution, which is the right choice for most
/// Bluetooth/thermal label printers since they usually only support one or two fixed
/// native resolutions (commonly 203, 300, or 600 dpi) rather than an arbitrary value.
/// </summary>
public record ShippingLabelsPrintRequest(
    string PrinterName,
    int Count = 1,
    string? PageSize = null,
    PageOrientation Orientation = PageOrientation.Landscape,
    int? Dpi = null,
    PrintFitMode FitMode = PrintFitMode.Fit);