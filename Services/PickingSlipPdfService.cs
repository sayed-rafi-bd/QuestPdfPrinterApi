using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace QuestPdfPrinterApi.Services;

public interface IPickingSlipPdfService
{
    IDocument BuildSlipsDocument(List<PickingSlip> slips, string? pageSizeName = null);
}

/// <summary>
/// Builds one picking/inspection slip (検品書) per PickingSlip, each on its own page. This
/// document is normally run on plain sheet stock, so page size goes through
/// PageSizeResolver the same way a generic report would - callers can request A4, Letter,
/// etc., defaulting to A4.
///
/// FONT: this slip is Japanese-first (JAN/商品名/様 etc. throughout), so it can't use
/// QuestPDF's built-in "Helvetica" (no CJK glyphs - Japanese text would render as
/// tofu/blank boxes). DefaultFontFamily must name a font
/// that's been registered before any PDF is generated - see the Fonts:NotoSansJpPath
/// bundling setup in Program.cs, which follows the same "drop it in Tools/, read the path
/// from appsettings.json" pattern WindowsPrinterService uses for SumatraPDF.exe.
/// </summary>
public class PickingSlipPdfService : IPickingSlipPdfService
{
    public const string DefaultFontFamily = "Noto Sans JP";

    public IDocument BuildSlipsDocument(List<PickingSlip> slips, string? pageSizeName = null)
    {
        if (!PageSizeResolver.TryResolve(pageSizeName, out var pageSize, out var error))
            throw new ArgumentException(error);

        return Document.Create(container =>
        {
            foreach (var slip in slips)
                BuildSlipPage(container, slip, pageSize);
        });
    }

    private static void BuildSlipPage(IDocumentContainer container, PickingSlip slip, PageSize pageSize)
    {
        container.Page(page =>
        {
            page.Size(pageSize);
            page.MarginVertical(28);
            page.MarginHorizontal(32);
            page.DefaultTextStyle(x => x.FontFamily(DefaultFontFamily));

            // Two layers: the primary one flows top-down (header, addressee, item table)
            // and grows/shrinks with however many items there are; the footer is a second
            // layer pinned to the bottom of the page, matching the fixed-position carrier
            // code / item-count / total-quantity row at the bottom of the source slip
            // regardless of how much (or little) of the page the item table fills.
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Column(col => BuildMainContent(col, slip));
                layers.Layer().AlignBottom().Element(e => BuildFooter(e, slip));
            });
        });
    }

    private static void BuildMainContent(ColumnDescriptor col, PickingSlip slip)
    {
        col.Spacing(6);

        // PicNo / inspection date / delivery-note flag / page counter / carrier barcode
        col.Item().Row(row =>
        {
            row.RelativeItem(3).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.AutoItem().Text("PicNo.").FontSize(11).Bold();
                    r.ConstantItem(6);
                    r.AutoItem().Text(slip.PicNo).FontSize(14).Bold();
                });
                c.Item().Row(r =>
                {
                    r.AutoItem().Text("検品日").FontSize(11).Bold();
                    r.ConstantItem(6);
                    r.AutoItem().Text(slip.InspectionDate.ToString("yyyy/MM/dd")).FontSize(13);
                });
            });

            row.RelativeItem(2).Column(c =>
            {
                if (slip.DeliveryNoteEnclosed)
                    c.Item().AlignRight().Text("納品書在中").FontSize(13).Bold();

                c.Item().AlignRight().Text($"{slip.PageNumber}/{slip.PageCount}").FontSize(13).Bold();
            });

            row.ConstantItem(150).Column(c =>
            {
                c.Item().Height(45).Image(BuildCarrierBarcode(slip.CarrierCode));
                c.Item().AlignCenter().Text($"*{slip.CarrierCode}*").FontSize(10);
            });
        });

        // Warehouse / client code
        col.Item().Text(slip.WarehouseCode).FontSize(15);
        col.Item().Text(slip.ClientCode).FontSize(12);

        // Customer name - printed as-is, arbitrary punctuation included
        col.Item().Text($"{slip.CustomerName}\u3000様").FontSize(16);

        col.Item().PaddingTop(4).LineHorizontal(1.5f);

        // Item table
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(6);
                c.RelativeColumn(1);
            });

            table.Header(header =>
            {
                header.Cell().Text("JANコード").FontSize(11).Bold();
                header.Cell().Text("商品名").FontSize(11).Bold();
                header.Cell().AlignRight().Text("数量").FontSize(11).Bold();

                header.Cell().ColumnSpan(3).PaddingTop(2).LineHorizontal(1.5f);
            });

            foreach (var item in slip.Items)
            {
                table.Cell().PaddingVertical(2).Text(item.JanCode).FontSize(11);
                table.Cell().PaddingVertical(2).Text(item.ProductName).FontSize(11);
                table.Cell().PaddingVertical(2).AlignRight().Text(item.Quantity.ToString()).FontSize(11);
            }
        });
    }

    /// <summary>Carrier code/name + item/quantity totals row, pinned to the page bottom.</summary>
    private static void BuildFooter(IContainer container, PickingSlip slip)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"{slip.CarrierCode}　{slip.CarrierName}").FontSize(14).Bold();
            row.RelativeItem().AlignCenter().Text($"アイテム数　{slip.ItemCount}").FontSize(12);
            row.RelativeItem().AlignRight().Text($"合計数　{slip.TotalQuantity}").FontSize(12);
        });
    }

    /// <summary>
    /// Renders the carrier code as a Code39 barcode (PNG bytes) via ZXing.Net + SkiaSharp -
    /// same ZXing.Net + SkiaSharp workaround used for other Code128/Code39 barcodes, just a
    /// different symbology. Code39 is what produces the leading/trailing "*" the slip prints
    /// as the human-readable caption (Code39's own start/stop character), matching the
    /// "*054*" seen beneath the barcode on the source document.
    /// </summary>
    private static byte[] BuildCarrierBarcode(string carrierCode)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_39,
            Options = new EncodingOptions
            {
                Height = 90,
                Width = 300,
                Margin = 0,
                PureBarcode = true
            }
        };

        var pixelData = writer.Write(carrierCode);

        using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
