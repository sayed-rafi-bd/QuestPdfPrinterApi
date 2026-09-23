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
    IDocument BuildLabelsDocument(List<ShippingLabel> labels, PageSize pageSize);
}

/// <summary>
/// Builds one carrier shipping label (送り状) per ShippingLabel, each on its own page.
/// Unlike the picking slip this replaced, this is normally run on pre-cut label stock at a
/// fixed physical size (110mm x 84mm by default - see PageSizeResolver.BuildDefault), not
/// plain sheet stock, so the caller-facing endpoints resolve PageSize/orientation once (via
/// PageSizeResolver) and hand the concrete PageSize in here rather than this service
/// re-resolving a name itself.
///
/// FONT: Japanese-first (お届先/様/運送会社 etc. throughout), so - same as the picking slip
/// before it - DefaultFontFamily must name a font registered before any PDF is generated;
/// see the Fonts:NotoSansJpPath bundling setup in Program.cs.
/// </summary>
public class ShippingLabelPdfService : IShippingLabelPdfService
{
    public const string DefaultFontFamily = "Noto Sans JP";

    public IDocument BuildLabelsDocument(List<ShippingLabel> labels, PageSize pageSize)
    {
        return Document.Create(container =>
        {
            foreach (var label in labels)
                BuildLabelPage(container, label, pageSize);
        });
    }

    private static void BuildLabelPage(IDocumentContainer container, ShippingLabel label, PageSize pageSize)
    {
        container.Page(page =>
        {
            page.Size(pageSize);
            page.Margin(8);
            page.DefaultTextStyle(x => x.FontFamily(DefaultFontFamily));

            // The reference design is a single thick outer border with the detail grid
            // (お問合せNo. down through 出荷依頼番号) as one bordered box inside it - see
            // BuildDetailBox for how that inner box's own row/column dividers are built.
            page.Content().Border(1.75f).Padding(8).Column(col =>
            {
                col.Spacing(3);
                col.Item().Element(e => BuildHeader(e, label));
                col.Item().PaddingTop(2).Text($"{label.RecipientName}\u3000様").FontSize(15);
                col.Item().PaddingTop(2).Element(e => BuildDetailBox(e, label));
            });
        });
    }

    /// <summary>お届先 address block (left) + sort code / TEL / carrier name (right).</summary>
    private static void BuildHeader(IContainer container, ShippingLabel label)
    {
        container.Row(row =>
        {
            row.RelativeItem(3).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.AutoItem().Text("お届先").FontSize(10);
                    r.ConstantItem(8);
                    r.AutoItem().Height(22).Image(BuildAddressBarcode(label.RecipientPostalCode));
                });
                c.Item().Text($"\u3012{label.RecipientPostalCode}").FontSize(11);
                c.Item().Text(label.RecipientAddress).FontSize(11.5f);
            });

            row.RelativeItem(2).Column(c =>
            {
                c.Item().AlignRight().Row(r =>
                {
                    r.AutoItem().Text(label.SortCodeMain).FontSize(30).Bold();
                    r.AutoItem().AlignBottom().PaddingBottom(2).Text(label.SortCodeSuffix).FontSize(15).Bold();
                });
                c.Item().AlignRight().Text($"TEL:{label.RecipientTel}").FontSize(9);
                c.Item().AlignRight().PaddingTop(2).Text(label.CarrierDisplayName).FontSize(24).Bold();
            });
        });
    }

    /// <summary>
    /// The bordered detail grid: tracking barcode + piece count, carrier/service name, and
    /// a date/time block alongside a sender block. Borders are drawn per-cell (BorderBottom/
    /// BorderLeft) rather than as one rigid table so each row can split its columns at a
    /// different point - matching the reference design, where the お問合せNo. divider and
    /// the sender-box divider don't line up with each other.
    /// </summary>
    private static void BuildDetailBox(IContainer container, ShippingLabel label)
    {
        container.Border(1f).Column(col =>
        {
            col.Item().BorderBottom(0.75f).Padding(4).Row(row =>
            {
                row.RelativeItem(4).Column(c =>
                {
                    c.Item().Text("お問合せ No.").FontSize(9);
                    c.Item().Height(20).Image(BuildTrackingBarcode(label.TrackingNumber));
                    c.Item().AlignCenter().Text($"a{label.TrackingNumber}a").FontSize(8).LetterSpacing(0.12f);
                });
                row.ConstantItem(1);
                row.RelativeItem(1).BorderLeft(0.75f).PaddingLeft(6).Column(c =>
                {
                    c.Item().Text("総個数").FontSize(9);
                    c.Item().AlignCenter().Row(r =>
                    {
                        r.AutoItem().Text(label.TotalPieces.ToString()).FontSize(20).Bold();
                        r.AutoItem().AlignBottom().Text("個口").FontSize(9);
                    });
                });
            });

            col.Item().BorderBottom(0.75f).Padding(4).Row(row =>
            {
                row.AutoItem().Text("運送会社").FontSize(9);
                row.ConstantItem(8);
                row.AutoItem().Text(label.CarrierServiceName).FontSize(11);
            });

            col.Item().Row(row =>
            {
                row.RelativeItem(1).Column(c =>
                {
                    c.Item().BorderBottom(0.75f).Padding(4).Element(e => BuildDateBlock(e, label));
                    c.Item().Padding(4).Element(e => BuildRemarksBlock(e, label));
                });
                row.RelativeItem(1).BorderLeft(0.75f).Padding(4).Element(e => BuildSenderBlock(e, label));
            });
        });
    }

    private static void BuildDateBlock(IContainer container, ShippingLabel label)
    {
        container.Column(c =>
        {
            c.Item().Row(r =>
            {
                r.ConstantItem(60).Text("出荷日").FontSize(9);
                r.AutoItem().Text(label.ShipDate.ToString("yyyy/MM/dd")).FontSize(10);
            });
            c.Item().Row(r =>
            {
                r.ConstantItem(60).Text("お届指定日").FontSize(9);
                r.AutoItem().Text(label.DeliveryDate.ToString("yyyy/MM/dd")).FontSize(10);
            });
            c.Item().Row(r =>
            {
                r.ConstantItem(60).Text("時間帯指定").FontSize(9);
                r.AutoItem().Text(label.TimeSlot).FontSize(10);
            });
        });
    }

    private static void BuildRemarksBlock(IContainer container, ShippingLabel label)
    {
        container.Column(c =>
        {
            c.Item().Text("[備考]").FontSize(9);
            if (label.CodAmountYen is int yen)
                c.Item().AlignCenter().Text($"\u00a5{yen:N0}").FontSize(16).Bold();
            if (!string.IsNullOrEmpty(label.ShippingRequestNumber))
                c.Item().Text($"出荷依頼番号: {label.ShippingRequestNumber}").FontSize(8.5f);
        });
    }

    private static void BuildSenderBlock(IContainer container, ShippingLabel label)
    {
        container.Column(c =>
        {
            c.Item().Text(label.SenderName).FontSize(9.5f);
            c.Item().Text($"\u3012{label.SenderPostalCode}").FontSize(9);
            c.Item().Text(label.SenderAddressLine1).FontSize(9.5f);
            if (!string.IsNullOrEmpty(label.SenderAddressLine2))
                c.Item().Text(label.SenderAddressLine2).FontSize(9.5f);
            c.Item().Text($"TEL: {label.SenderTel}").FontSize(9);
        });
    }

    /// <summary>
    /// Renders the recipient postal code as a Code128 barcode (PNG bytes) via ZXing.Net +
    /// SkiaSharp - same workaround the picking slip used for its Code39 carrier barcode,
    /// just a different symbology. No human-readable caption is printed under this one in
    /// the reference design (unlike the tracking barcode below), so PureBarcode strips
    /// ZXing's own text row.
    /// </summary>
    private static byte[] BuildAddressBarcode(string postalCode)
    {
        return RenderBarcode(BarcodeFormat.CODE_128, postalCode, width: 260, height: 90);
    }

    /// <summary>
    /// Renders the お問合せNo. tracking number as a Codabar (NW-7) barcode. Codabar is the
    /// symbology that produces the leading/trailing "a" start/stop characters seen in the
    /// human-readable caption beneath it in the reference design ("a223456780131a") -
    /// ZXing's Codabar writer requires those start/stop characters to already be present in
    /// the payload it's given, so they're added here rather than left to the caller.
    /// </summary>
    private static byte[] BuildTrackingBarcode(string trackingNumber)
    {
        return RenderBarcode(BarcodeFormat.CODABAR, $"a{trackingNumber}a", width: 320, height: 90);
    }

    private static byte[] RenderBarcode(BarcodeFormat format, string payload, int width, int height)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = format,
            Options = new EncodingOptions
            {
                Height = height,
                Width = width,
                Margin = 0,
                PureBarcode = true
            }
        };

        var pixelData = writer.Write(payload);

        using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
