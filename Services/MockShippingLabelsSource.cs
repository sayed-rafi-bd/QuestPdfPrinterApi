using QuestPdfPrinterApi.Models;

namespace QuestPdfPrinterApi.Services;

public interface IMockShippingLabelsSource
{
    List<ShippingLabel> GenerateLabels(int count);
}

/// <summary>
/// Backs the mock GET /api/mock/shipping-labels endpoint as a self-hosted stand-in for a
/// real data source. Generates `count` random-but-plausible labels so it needs no database -
/// swap for a real repository later without touching the endpoint or the PDF code.
/// </summary>
public class MockShippingLabelsSource : IMockShippingLabelsSource
{
    private static readonly Random Random = new();

    private static readonly (string Code, string Suffix, string ServiceName)[] Carriers =
    {
        ("7013", "342", "佐川代引（LC）"),
        ("2456", "108", "佐川急便"),
        ("6690", "071", "ヤマト運輸（コレクト）")
    };

    private static readonly (string Postal, string Address)[] RecipientAddresses =
    {
        ("150-0021", "東京都渋谷区恵比寿西1-13-7 KB's ARCH"),
        ("530-0001", "大阪府大阪市北区梅田2-4-9 グランドビル"),
        ("460-0008", "愛知県名古屋市中区栄3-15-33 サカエタワー")
    };

    private static readonly string[] RecipientNames = { "トントンタン", "山田太郎", "鈴木花子", "佐藤商店" };

    private static readonly (string Name, string Postal, string Line1, string Line2, string Tel)[] Senders =
    {
        ("FMHテスト用(テスト環境00)", "150-0021", "東京都渋谷区恵比寿西1-13-7", "KBSアーチビル", "0357840340"),
        ("FMH物流センター", "460-0008", "愛知県名古屋市中区栄3-15-33", "サカエタワー5F", "0522345678")
    };

    public List<ShippingLabel> GenerateLabels(int count)
    {
        return Enumerable.Range(1, count)
            .Select(_ =>
            {
                var carrier = Carriers[Random.Next(Carriers.Length)];
                var recipient = RecipientAddresses[Random.Next(RecipientAddresses.Length)];
                var sender = Senders[Random.Next(Senders.Length)];
                var shipDate = DateTime.Today;

                return new ShippingLabel
                {
                    RecipientPostalCode = recipient.Postal,
                    RecipientAddress = recipient.Address,
                    SortCodeMain = carrier.Code,
                    SortCodeSuffix = carrier.Suffix,
                    RecipientTel = $"0{Random.Next(70, 91)}{Random.Next(1000, 9999)}{Random.Next(1000, 9999)}",
                    CarrierDisplayName = "佐川",
                    RecipientName = RecipientNames[Random.Next(RecipientNames.Length)],
                    TrackingNumber = Random.NextInt64(100_000_000_000, 999_999_999_999).ToString(),
                    TotalPieces = Random.Next(1, 4),
                    CarrierServiceName = carrier.ServiceName,
                    ShipDate = shipDate,
                    DeliveryDate = shipDate.AddDays(2),
                    TimeSlot = "16時～18時",
                    SenderName = sender.Name,
                    SenderPostalCode = sender.Postal,
                    SenderAddressLine1 = sender.Line1,
                    SenderAddressLine2 = sender.Line2,
                    SenderTel = sender.Tel,
                    CodAmountYen = Random.Next(0, 2) == 0 ? null : Random.Next(500, 10_000),
                    ShippingRequestNumber = Random.Next(100_000_000, 999_999_999).ToString()
                };
            })
            .ToList();
    }

    /// <summary>
    /// Reproduces the specific reference label this template was validated against
    /// (150-0021 / 佐川 / 7013342 / a223456780131a), for one-off regression checks whenever
    /// the layout changes. Not wired to an endpoint.
    /// </summary>
    public ShippingLabel GenerateReferenceLabel()
    {
        return new ShippingLabel
        {
            RecipientPostalCode = "150-0021",
            RecipientAddress = "東京都渋谷区恵比寿西1-13-7 KB's ARCH",
            SortCodeMain = "7013",
            SortCodeSuffix = "342",
            RecipientTel = "08012346677",
            CarrierDisplayName = "佐川",
            RecipientName = "トントンタン",
            TrackingNumber = "223456780131",
            TotalPieces = 1,
            CarrierServiceName = "佐川代引（LC）",
            ShipDate = new DateTime(2025, 7, 3),
            DeliveryDate = new DateTime(2025, 7, 5),
            TimeSlot = "16時～18時",
            SenderName = "FMHテスト用(テスト環境00)",
            SenderPostalCode = "150-0021",
            SenderAddressLine1 = "東京都渋谷区恵比寿西1-13-7",
            SenderAddressLine2 = "KBSアーチビル",
            SenderTel = "0357840340",
            CodAmountYen = 3960,
            ShippingRequestNumber = "875162700"
        };
    }
}
