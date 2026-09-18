using QuestPDF.Helpers;
using QuestPdfPrinterApi.Models;

namespace QuestPdfPrinterApi.Services;

public interface IMockPickingSlipsSource
{
    List<PickingSlip> GenerateSlips(int count);
}

/// <summary>
/// Backs the mock GET /api/mock/picking-slips endpoint as a self-hosted stand-in for a
/// real data source. Generates `count` random-but-plausible slips using
/// QuestPDF.Helpers.Placeholders for the free-text fields so it needs no database - swap
/// for a real repository later without touching the endpoint or the PDF code.
/// </summary>
public class MockPickingSlipsSource : IMockPickingSlipsSource
{
    private static readonly Random Random = new();

    private static readonly (string Code, string Name)[] Carriers =
    {
        ("054", "ヤマト運輸(LA)"),
        ("061", "佐川急便"),
        ("003", "日本郵便")
    };

    private static readonly string[] WarehouseCodes = { "Aichi", "Tokyo", "Osaka", "Fukuoka" };
    private static readonly string[] ClientCodes = { "FMH29", "FMH31", "FMH07" };

    private static readonly (string Jan, string Name)[] SampleProducts =
    {
        ("3474636954766", "EC専用SP バンディバレントR 250ml"),
        ("4901234567894", "洗顔フォーム 詰め替え用 130g"),
        ("4912345098765", "保湿クリーム ジャー 50g"),
        ("4909876543210", "ヘアオイル 100ml")
    };

    public List<PickingSlip> GenerateSlips(int count)
    {
        return Enumerable.Range(1, count)
            .Select(_ =>
            {
                var carrier = Carriers[Random.Next(Carriers.Length)];
                var itemCount = Random.Next(1, 4);
                var items = Enumerable.Range(0, itemCount)
                    .Select(_ =>
                    {
                        var product = SampleProducts[Random.Next(SampleProducts.Length)];
                        return new PickingSlipItem
                        {
                            JanCode = product.Jan,
                            ProductName = product.Name,
                            Quantity = Random.Next(1, 5)
                        };
                    })
                    .ToList();

                return new PickingSlip
                {
                    PicNo = $"{Random.Next(100_000_000, 999_999_999)}-1",
                    InspectionDate = DateTime.Now,
                    DeliveryNoteEnclosed = true,
                    PageNumber = 1,
                    PageCount = 1,
                    WarehouseCode = WarehouseCodes[Random.Next(WarehouseCodes.Length)],
                    ClientCode = ClientCodes[Random.Next(ClientCodes.Length)],
                    CustomerName = Placeholders.Label(),
                    CarrierCode = carrier.Code,
                    CarrierName = carrier.Name,
                    Items = items
                };
            })
            .ToList();
    }

    /// <summary>
    /// Reproduces the specific edge-case slip this template was validated against: a
    /// customer name packed with full-width characters, HTML-entity-shaped text, quotes,
    /// and angle brackets, to confirm the field renders as literal text rather than being
    /// interpreted, truncated, or corrupting the surrounding layout. Not wired to an
    /// endpoint - call it directly for a one-off regression check whenever the layout for
    /// that field changes.
    /// </summary>
    public PickingSlip GenerateSpecialCharacterTestSlip()
    {
        return new PickingSlip
        {
            PicNo = "877250700-1",
            InspectionDate = new DateTime(2026, 9, 16),
            DeliveryNoteEnclosed = true,
            PageNumber = 1,
            PageCount = 1,
            WarehouseCode = "Aichi",
            ClientCode = "FMH29",
            CustomerName = "ＦＭＨテスト用（あ＆＃１２\"＃ｃＡｂｃ<>'\"'&'+40",
            CarrierCode = "054",
            CarrierName = "ヤマト運輸(LA)",
            Items = new List<PickingSlipItem>
            {
                new()
                {
                    JanCode = "3474636954766",
                    ProductName = "EC専用SP バンディバレントR 250ml",
                    Quantity = 1
                }
            }
        };
    }
}
