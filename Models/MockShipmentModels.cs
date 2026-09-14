namespace QuestPdfPrinterApi.Models;

// Shape returned by GET /api/mock/shipments - a self-hosted stand-in "external" API
// endpoint. count controls how many objects come back: count=1 -> a 1-item array,
// count=10 -> a 10-item array, and so on. Each shipment renders as exactly one 4x6in
// label page (see ShippingLabelPdfService), so count == page count here.

public class Address
{
    public string Name { get; set; } = "";
    public string Line1 { get; set; } = "";
    public string? Line2 { get; set; }
    public string CityStateZip { get; set; } = "";
    public string Country { get; set; } = "";
}

public class MockShipment
{
    public string TrackingNumber { get; set; } = "";
    public string OrderReference { get; set; } = "";
    public string Carrier { get; set; } = "";
    public string Service { get; set; } = "";
    public decimal WeightKg { get; set; }
    public DateTime ShipDate { get; set; }
    public Address From { get; set; } = new();
    public Address To { get; set; } = new();
}

/// <summary>
/// Body for POST /api/mock/shipments/preview. No userId - Count controls how many mock
/// shipment labels are generated (same as GET /api/mock/shipments?count=N).
/// </summary>
public record MockShipmentsPreviewRequest(int Count = 1, int? CompanionPort = null);

/// <summary>
/// Body for POST /api/mock/shipments/print. Same Count contract as
/// MockShipmentsPreviewRequest. PrinterName is required - use GET /api/printers to see
/// what's available.
/// </summary>
public record MockShipmentsPrintRequest(string PrinterName, int Count = 1);
