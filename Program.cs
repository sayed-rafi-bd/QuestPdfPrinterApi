using System.Text.Json.Serialization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;
using QuestPdfPrinterApi.Services;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// So PageOrientation is accepted/returned as "Portrait"/"Landscape" in
// JSON request/response bodies instead of raw integers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<IMockShippingLabelsSource, MockShippingLabelsSource>();
builder.Services.AddSingleton<IShippingLabelPdfService, ShippingLabelPdfService>();
builder.Services.AddSingleton<IPrinterService, PrinterService>();
builder.Services.AddSingleton<IDocumentDeliveryService, DocumentDeliveryService>();

var app = builder.Build();

// Bundle a CJK font the same way PrinterService bundles SumatraPDF.exe: drop the
// .ttf/.otf in Tools/Fonts (copied next to the app's binaries by the .csproj), point
// "Fonts:NotoSansJpPath" at it if the location differs. ShippingLabelPdfService's Japanese
// text needs this registered before any PDF is generated - QuestPDF.Helpers's builtin
// "Helvetica" has no CJK glyphs. Best-effort and non-fatal: log and continue if the font
// isn't there yet, so the missing-glyph symptom is easy to diagnose from the log.
var fontPath = app.Configuration["Fonts:NotoSansJpPath"] ?? Path.Combine("Tools", "Fonts", "NotoSansJP-Regular.ttf");
var resolvedFontPath = Path.IsPathRooted(fontPath) ? fontPath : Path.Combine(AppContext.BaseDirectory, fontPath);
if (File.Exists(resolvedFontPath))
{
    using var fontStream = File.OpenRead(resolvedFontPath);
    QuestPDF.Drawing.FontManager.RegisterFont(fontStream);
}
else
{
    Console.Error.WriteLine(
        $"[FONT WARNING] '{resolvedFontPath}' not found - shipping labels (送り状) will render with " +
        $"missing glyphs until a font is placed there or \"Fonts:NotoSansJpPath\" is set.");
}

QuestPDF.Fluent.Document.Create(c => c.Page(p => p.Content().Text("warmup"))).GeneratePdf();

app.Use(async (context, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    // Server-Timing lets the UI show server time separately from the browser round trip.
    // Headers must be set before the response starts, hence OnStarting.
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["Server-Timing"] = $"app;dur={sw.ElapsedMilliseconds}";
        return Task.CompletedTask;
    });
    await next();
    sw.Stop();
    Console.WriteLine($"[TIMING] {context.Request.Method} {context.Request.Path}{context.Request.QueryString}: {sw.ElapsedMilliseconds} ms (status {context.Response.StatusCode})");
});

// Serves wwwroot/index.html (the UI) at "/".
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/printers", async (IPrinterService printers, CancellationToken ct) =>
{
    var found = await printers.GetAvailablePrintersAsync(ct);
    return Results.Ok(found);
})
.WithName("GetPrinters")
.WithSummary("List available printers")
.WithDescription("Returns every printer known to Windows (PowerShell's Get-Printer) - USB, network, and " +
                  "Bluetooth printers all show up here identically once installed as a Windows print " +
                  "queue. Each entry includes PortName and a best-effort IsBluetooth flag to help tell " +
                  "queues apart; see PrinterInfo's remarks for how IsBluetooth is detected.")
.Produces<List<PrinterInfo>>(StatusCodes.Status200OK);

// ---- Shipping labels ----
// Labels come from IMockShippingLabelsSource, called in-process as a service (there is no
// longer a /api/mock/shipping-labels route). Each label is one page, and every PDF is built
// at the label template's own page size (ShippingLabelPdfService.TemplatePageSize) - no
// endpoint takes a pageSize for the generated PDF.

// GET /api/mock/shipping-labels/pdf?count=1 - generates mock labels in-process and turns
// them straight into label pages (one label per page) at the template's page size
app.MapGet("/api/mock/shipping-labels/pdf", (IMockShippingLabelsSource source, IShippingLabelPdfService pdf, int count = 1) =>
{
    if (count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var labels = source.GenerateLabels(count);

    var document = pdf.BuildLabelsDocument(labels);
    var bytes = document.GeneratePdf();
    return Results.File(bytes, "application/pdf", $"shipping-labels-{count}.pdf");
})
.WithName("DownloadMockShippingLabelsPdf")
.WithSummary("Download the mock shipping labels PDF directly")
.WithDescription("count: number of labels/pages, >= 1 (default 1). The PDF's page size is always the label template's size (ISO C7, 114x81mm landscape).")
.Produces(StatusCodes.Status200OK, contentType: "application/pdf")
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/shipping-labels/preview - build one mock shipping label PDF (template page
// size) and push it to the Companion App
app.MapPost("/api/mock/shipping-labels/preview", (ShippingLabelsPreviewRequest request, IMockShippingLabelsSource source, IShippingLabelPdfService pdf, IDocumentDeliveryService delivery) =>
{
    var labels = source.GenerateLabels(1);
    var document = pdf.BuildLabelsDocument(labels);
    return delivery.Preview(document, request.CompanionPort, "shipping label");
})
.WithName("PreviewMockShippingLabels")
.WithSummary("Preview a mock shipping label in the Companion App")
.WithDescription("Always previews 1 label at the template's page size. companionPort: Companion App port (default 12500).")
.Produces(StatusCodes.Status200OK);

// POST /api/mock/shipping-labels/print - build the mock shipping labels PDF and send it straight to a printer.
// pageSize/orientation are print-only here: they're forwarded as-is to SumatraPDF
// (see PrinterService.PrintFileAsync) and never change the PDF's own page geometry, which is
// always the template's page size. That also means pageSize isn't limited to any named
// sizes - any "paper=" value SumatraPDF/the driver accepts works. Printing is always
// 1:1 ("noscale"), so there is no fit mode to choose.
app.MapPost("/api/mock/shipping-labels/print", async (ShippingLabelsPrintRequest request, IMockShippingLabelsSource source, IShippingLabelPdfService pdf, IDocumentDeliveryService delivery, CancellationToken ct) =>
{
    if (request.Count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var labels = source.GenerateLabels(request.Count);
    var document = pdf.BuildLabelsDocument(labels);
    return await delivery.PrintAsync(document, request.PrinterName, $"shipping labels ({labels.Count} label(s))", request.Dpi, request.Orientation, request.PageSize, ct);
})
.WithName("PrintMockShippingLabels")
.WithSummary("Print the mock shipping labels")
.WithDescription("printerName: required, see GET /api/printers. count: number of labels/pages, >= 1 (default 1). dpi: optional, omit for the printer's own default resolution. pageSize and orientation are print-only - forwarded to SumatraPDF, not used to resize the PDF: pageSize is any SumatraPDF paper value (e.g. A4, or a custom size like '76mm x 130mm'), omit for the printer's current paper; orientation is Landscape (default) or Portrait. Printing is always at 1:1 scale (no rescale).")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

app.Run();