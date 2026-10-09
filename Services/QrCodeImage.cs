using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace QuestPdfPrinterApi.Services;

/// <summary>
/// Renders text as a QR code PNG. Same ZXing.Net + SkiaSharp approach as the barcode in
/// ShippingLabelPdfService, so the UI doesn't need a QR library from a CDN (it works offline).
/// </summary>
public static class QrCodeImage
{
    public static byte[] RenderPng(string text, int size = 360)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions
            {
                Width = size,
                Height = size,
                Margin = 2
            }
        };

        var pixelData = writer.Write(text);

        using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
