using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace QuestPdfPrinterApi.Services;

public interface IShippingLabelPdfService
{
    IDocument BuildLabelsDocument(List<MockShipment> shipments);
}

/// <summary>
/// Builds one standard 4x6in shipping label per shipment, each on its own page. Unlike
/// the old products report, page size is fixed (labels are printed on 4x6 stock), so
/// there's no PageSizeResolver here - one shipment always maps to exactly one page.
/// </summary>
public class ShippingLabelPdfService : IShippingLabelPdfService
{
    private static readonly PageSize LabelSize = new(4, 6, Unit.Inch);

    public IDocument BuildLabelsDocument(List<MockShipment> shipments)
    {
        return Document.Create(container =>
        {
            foreach (var shipment in shipments)
                BuildLabelPage(container, shipment);
        });
    }

    private static void BuildLabelPage(IDocumentContainer container, MockShipment shipment)
    {
        container.Page(page =>
        {
            page.Size(LabelSize);
            page.MarginVertical(11);
            page.MarginHorizontal(11);
            page.DefaultTextStyle(x => x.FontFamily("Helvetica"));

            page.Content().Column(col =>
            {
                col.Spacing(6);

                // Carrier + service
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(shipment.Carrier.ToUpperInvariant()).FontSize(16).Bold();
                    row.AutoItem().Text(shipment.Service.ToUpperInvariant()).FontSize(12).Bold();
                });
                col.Item().LineHorizontal(1.5f);

                // FROM
                col.Item().Column(from =>
                {
                    from.Item().Text("FROM").FontSize(7).Bold();
                    from.Item().Text(shipment.From.Name).FontSize(8);
                    from.Item().Text(shipment.From.Line1).FontSize(8);
                    if (!string.IsNullOrWhiteSpace(shipment.From.Line2))
                        from.Item().Text(shipment.From.Line2!).FontSize(8);
                    from.Item().Text(shipment.From.CityStateZip).FontSize(8);
                    from.Item().Text(shipment.From.Country).FontSize(8);
                });
                col.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);

                // SHIP TO - bigger and bold, this is what matters for delivery
                col.Item().Column(to =>
                {
                    to.Item().Text("SHIP TO").FontSize(8).Bold();
                    to.Item().Text(shipment.To.Name).FontSize(13).Bold();
                    to.Item().Text(shipment.To.Line1).FontSize(13).Bold();
                    if (!string.IsNullOrWhiteSpace(shipment.To.Line2))
                        to.Item().Text(shipment.To.Line2!).FontSize(13).Bold();
                    to.Item().Text(shipment.To.CityStateZip).FontSize(13).Bold();
                    to.Item().Text(shipment.To.Country).FontSize(13).Bold();
                });
                col.Item().LineHorizontal(1.5f);

                // Weight / service / ship date row
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("WEIGHT").FontSize(7).Bold();
                        c.Item().Text($"{shipment.WeightKg:0.0} kg").FontSize(9);
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("SERVICE").FontSize(7).Bold();
                        c.Item().Text(shipment.Service).FontSize(9);
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("SHIP DATE").FontSize(7).Bold();
                        c.Item().Text(shipment.ShipDate.ToString("yyyy-MM-dd")).FontSize(9);
                    });
                });
                col.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);

                // Tracking number, human-readable + barcode
                col.Item().Text("TRACKING #").FontSize(8).Bold();
                col.Item().Text(shipment.TrackingNumber).FontSize(11).Bold();
                col.Item().AlignCenter().Height(55).Image(BuildTrackingBarcode(shipment.TrackingNumber));

                col.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);

                // Footer
                col.Item().Text($"Order #: {shipment.OrderReference}")
                    .FontSize(7).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    /// <summary>
    /// Renders the tracking number as a Code128 barcode (PNG bytes) via ZXing.Net +
    /// SkiaSharp. QuestPDF has no built-in barcode primitive, so this is the standard
    /// workaround: encode to pixel data, wrap it in an SKBitmap, and re-encode as PNG for
    /// QuestPDF's Image() element. Requires the ZXing.Net and SkiaSharp package
    /// references added to the .csproj.
    /// </summary>
    private static byte[] BuildTrackingBarcode(string trackingNumber)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Height = 140,
                Width = 640,
                Margin = 0,
                PureBarcode = true
            }
        };

        var pixelData = writer.Write(trackingNumber);

        using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
