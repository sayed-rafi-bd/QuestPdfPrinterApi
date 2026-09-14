using QuestPDF.Helpers;
using QuestPdfPrinterApi.Models;

namespace QuestPdfPrinterApi.Services;

public interface IMockShipmentsSource
{
    List<MockShipment> GenerateShipments(int count);
}

/// <summary>
/// Backs the mock GET /api/mock/shipments endpoint. Generates `count` random-but-plausible
/// shipments using QuestPDF.Helpers.Placeholders (the same generator style used elsewhere)
/// so it needs no database or external service - swap this for a real repository later
/// without touching the endpoint or the PDF code.
/// </summary>
public class MockShipmentsSource : IMockShipmentsSource
{
    private static readonly Random Random = new();
    private static readonly string[] Services = { "Priority", "Standard", "Express", "Economy" };
    private static readonly string[] CountryCodes = { "US", "JP", "GB", "DE", "CA" };

    // Fixed warehouse address - the "FROM" side of every label. Swap for real
    // origin/warehouse data later.
    private static readonly Address FromAddress = new()
    {
        Name = "Acme Fulfillment Co.",
        Line1 = "500 Industrial Pkwy",
        CityStateZip = "Springfield, IL 62704",
        Country = "US"
    };

    public List<MockShipment> GenerateShipments(int count)
    {
        return Enumerable.Range(1, count)
            .Select(_ =>
            {
                var countryCode = CountryCodes[Random.Next(CountryCodes.Length)];

                return new MockShipment
                {
                    TrackingNumber = $"MS{Random.Next(100_000_000, 999_999_999)}{countryCode}",
                    OrderReference = $"SO-{DateTime.Now:yyyy}-{Random.Next(1000, 9999)}",
                    Carrier = "MockShip",
                    Service = Services[Random.Next(Services.Length)],
                    WeightKg = (decimal)Math.Round(Random.NextDouble() * 10 + 0.2, 1),
                    ShipDate = DateTime.Now,
                    From = FromAddress,
                    To = new Address
                    {
                        Name = Placeholders.Label(),
                        Line1 = Placeholders.Label(),
                        CityStateZip = Placeholders.Label(),
                        Country = countryCode
                    }
                };
            })
            .ToList();
    }
}
