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
/// fixed physical size (ISO C7 (114mm x 81mm) by default - see PageSizeResolver.BuildDefault), not
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

    // Centralized palette so the whole label reads as one coherent design instead of every
    // border/caption picking its own shade. Kept to greys (no hue) on purpose: most of these
    // labels end up on thermal/monochrome label printers, where a "light blue caption" would
    // just print as an unpredictable grey anyway - true greys dither predictably instead.
    private static readonly string OuterBorderColor = Colors.Black;
    private static readonly string DividerColor = Colors.Grey.Lighten1;
    private static readonly string CaptionColor = Colors.Grey.Darken2;
    private static readonly string ChipBackgroundColor = Colors.Grey.Lighten4;
    private static readonly string AccentBackgroundColor = Colors.Grey.Lighten3;

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
            page.DefaultTextStyle(x => x.FontFamily(DefaultFontFamily).FontColor(Colors.Black));

            // The reference design is a single thick outer border with the detail grid
            // (お問合せNo. down through 出荷依頼番号) as one bordered box inside it - see
            // BuildDetailBox for how that inner box's own row/column dividers are built.
            page.Content().Border(1.75f).BorderColor(OuterBorderColor).Padding(8).Column(col =>
            {
                col.Spacing(4);
                col.Item().Element(e => BuildHeader(e, label));
                col.Item().PaddingTop(1).BorderBottom(0.75f).BorderColor(DividerColor)
                    .PaddingBottom(3).Text($"{label.RecipientName}\u3000様").FontSize(16).Bold();
                col.Item().Element(e => BuildDetailBox(e, label));
            });
        });
    }

    /// <summary>Small field caption (お届先, 運送会社, 出荷日, ...) rendered in the shared caption color.</summary>
    private static void Caption(IContainer container, string text) =>
        container.Text(text).FontSize(9).FontColor(CaptionColor).LetterSpacing(0.02f);

    /// <summary>お届先 address block (left) + sort code / TEL / carrier name (right).</summary>
    private static void BuildHeader(IContainer container, ShippingLabel label)
    {
        container.Row(row =>
        {
            row.RelativeItem(3).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.AutoItem().Element(e => Caption(e, "お届先"));
                    r.ConstantItem(8);
                    r.AutoItem().Height(22).Image(BuildAddressBarcode(label.RecipientPostalCode));
                });
                c.Item().PaddingTop(2).Text($"\u3012{label.RecipientPostalCode}").FontSize(11);
                c.Item().Text(label.RecipientAddress).FontSize(11.5f).Bold();
            });

            row.RelativeItem(2).Column(c =>
            {
                c.Item().AlignRight().Row(r =>
                {
                    r.AutoItem().Text(label.SortCodeMain).FontSize(30).Bold();
                    r.AutoItem().AlignBottom().PaddingBottom(2).Text(label.SortCodeSuffix).FontSize(15).Bold();
                });
                c.Item().AlignRight().Text($"TEL:{label.RecipientTel}").FontSize(9).FontColor(CaptionColor);
                c.Item().AlignRight().PaddingTop(3).Background(AccentBackgroundColor)
                    .PaddingVertical(2).PaddingHorizontal(8)
                    .Text(label.CarrierDisplayName).FontSize(24).Bold();
            });
        });
    }

    /// <summary>
    /// The bordered detail grid: tracking barcode + piece count, carrier/service name, and
    /// a date/time block alongside a sender block. Borders are drawn per-cell (BorderBottom/
    /// BorderLeft) rather than as one rigid table so each row can split its columns at a
    /// different point - matching the reference design, where the お問合せNo. divider and
    /// the sender-box divider don't line up with each other. Dividers use the softer
    /// DividerColor rather than solid black so the grid reads as structure, not clutter,
    /// leaving black for the one border that should draw the eye (the outer frame).
    /// </summary>
    private static void BuildDetailBox(IContainer container, ShippingLabel label)
    {
        container.Border(1f).BorderColor(Colors.Grey.Darken1).Column(col =>
        {
            col.Item().BorderBottom(0.75f).BorderColor(DividerColor).Padding(5).Row(row =>
            {
                row.RelativeItem(4).Column(c =>
                {
                    c.Item().Element(e => Caption(e, "お問合せ No."));
                    c.Item().PaddingTop(1).Height(20).Image(BuildTrackingBarcode(label.TrackingNumber));
                    c.Item().AlignCenter().Text($"a{label.TrackingNumber}a").FontSize(8).LetterSpacing(0.12f);
                });
                row.ConstantItem(10);
                row.RelativeItem(1).BorderLeft(0.75f).BorderColor(DividerColor).PaddingLeft(8).Column(c =>
                {
                    c.Item().Element(e => Caption(e, "総個数"));
                    c.Item().AlignCenter().Background(ChipBackgroundColor).Padding(3).Row(r =>
                    {
                        r.AutoItem().Text(label.TotalPieces.ToString()).FontSize(20).Bold();
                        r.AutoItem().AlignBottom().PaddingBottom(1).Text("個口").FontSize(9);
                    });
                });
            });

            col.Item().BorderBottom(0.75f).BorderColor(DividerColor).Padding(5).Row(row =>
            {
                row.AutoItem().Element(e => Caption(e, "運送会社"));
                row.ConstantItem(8);
                row.AutoItem().Text(label.CarrierServiceName).FontSize(11).Bold();
            });

            col.Item().Row(row =>
            {
                row.RelativeItem(1).Column(c =>
                {
                    c.Item().BorderBottom(0.75f).BorderColor(DividerColor).Padding(5).Element(e => BuildDateBlock(e, label));
                    c.Item().Padding(5).Element(e => BuildRemarksBlock(e, label));
                });
                row.RelativeItem(1).BorderLeft(0.75f).BorderColor(DividerColor).Padding(5).Element(e => BuildSenderBlock(e, label));
            });
        });
    }

    private static void BuildDateBlock(IContainer container, ShippingLabel label)
    {
        container.Column(c =>
        {
            c.Spacing(1);
            c.Item().Row(r =>
            {
                r.ConstantItem(62).Element(e => Caption(e, "出荷日"));
                r.AutoItem().Text(label.ShipDate.ToString("yyyy/MM/dd")).FontSize(10);
            });
            c.Item().Row(r =>
            {
                r.ConstantItem(62).Element(e => Caption(e, "お届指定日"));
                r.AutoItem().Text(label.DeliveryDate.ToString("yyyy/MM/dd")).FontSize(10);
            });
            c.Item().Row(r =>
            {
                r.ConstantItem(62).Element(e => Caption(e, "時間帯指定"));
                r.AutoItem().Text(label.TimeSlot).FontSize(10);
            });
        });
    }

    private static void BuildRemarksBlock(IContainer container, ShippingLabel label)
    {
        container.Column(c =>
        {
            c.Item().Element(e => Caption(e, "[備考]"));
            if (label.CodAmountYen is int yen)
            {
                c.Item().PaddingTop(1).AlignCenter().Border(0.75f).BorderColor(Colors.Grey.Darken1)
                    .Background(ChipBackgroundColor).PaddingVertical(2).PaddingHorizontal(10)
                    .Text($"\u00a5{yen:N0}").FontSize(16).Bold();
            }
            if (!string.IsNullOrEmpty(label.ShippingRequestNumber))
                c.Item().PaddingTop(1).Text($"出荷依頼番号: {label.ShippingRequestNumber}").FontSize(8.5f).FontColor(CaptionColor);
        });
    }

    private static void BuildSenderBlock(IContainer container, ShippingLabel label)
    {
        container.Column(c =>
        {
            c.Spacing(1);
            c.Item().Text(label.SenderName).FontSize(9.5f).Bold();
            c.Item().Text($"\u3012{label.SenderPostalCode}").FontSize(9).FontColor(CaptionColor);
            c.Item().Text(label.SenderAddressLine1).FontSize(9.5f);
            if (!string.IsNullOrEmpty(label.SenderAddressLine2))
                c.Item().Text(label.SenderAddressLine2).FontSize(9.5f);
            c.Item().Text($"TEL: {label.SenderTel}").FontSize(9).FontColor(CaptionColor);
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
