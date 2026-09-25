using System.Text.Json.Serialization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;
using QuestPdfPrinterApi.Services;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// So PageOrientation/PrintFitMode are accepted/returned as "Portrait"/"Contain" etc. in
// JSON request/response bodies (and shown that way in Swagger) instead of raw integers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<IMockShippingLabelsSource, MockShippingLabelsSource>();
builder.Services.AddSingleton<IShippingLabelPdfService, ShippingLabelPdfService>();
builder.Services.AddSingleton<IPrinterService, PrinterService>();
builder.Services.AddSingleton<IDocumentDeliveryService, DocumentDeliveryService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "QuestPDF Printer API",
        Version = "v1",
        Description = "Builds Japanese carrier shipping labels (送り状) with QuestPDF, from mock data " +
                      "only. Download, preview, and print are all backed by their respective mock " +
                      "sources - preview and print each have their own endpoint."
    });
});

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
    await next();
    sw.Stop();
    Console.WriteLine($"[TIMING] {context.Request.Method} {context.Request.Path}{context.Request.QueryString}: {sw.ElapsedMilliseconds} ms (status {context.Response.StatusCode})");
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "QuestPDF Printer API v1");
    options.RoutePrefix = "swagger";
});

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

// ---- Mock shipping labels API (self-hosted stand-in "external" data source) ----
// count controls the array length: count=1 -> 1 object, count=10 -> 10 objects. Each
// label is one page, so count == page count for the PDF endpoints below. /pdf and /preview
// resolve pageSize via PageSizeResolver and hand the PDF service a concrete PageSize (the
// default is this API's ISO C7 (114mm x 81mm) label, not A4). /print always builds at that
// same default and instead forwards pageSize/orientation/fitMode straight to SumatraPDF -
// see its own handler below.

// GET /api/mock/shipping-labels?count=1 - the mock API route itself
app.MapGet("/api/mock/shipping-labels", (IMockShippingLabelsSource source, int count = 1) =>
{
    if (count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var labels = source.GenerateLabels(count);
    return Results.Ok(labels);
})
.WithName("GetMockShippingLabels")
.WithSummary("Mock shipping labels API")
.WithDescription("Stand-in for an external API. Returns a JSON array of randomly generated carrier shipping labels (送り状) - count=1 gives a 1-item array, count=10 gives a 10-item array, and so on.")
.Produces<List<ShippingLabel>>(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// GET /api/mock/shipping-labels/pdf?count=1&pageSize=A4 - generates mock labels in-process
// and turns them straight into label pages (one label per page)
app.MapGet("/api/mock/shipping-labels/pdf", (IMockShippingLabelsSource source, IShippingLabelPdfService pdf, int count = 1, string? pageSize = null) =>
{
    if (count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    if (!PageSizeResolver.TryResolve(pageSize, PageOrientation.Landscape, out var resolvedPageSize, out var pageSizeError))
        return Results.BadRequest(new { error = pageSizeError });

    var labels = source.GenerateLabels(count);

    var document = pdf.BuildLabelsDocument(labels, resolvedPageSize);
    var bytes = document.GeneratePdf();
    return Results.File(bytes, "application/pdf", $"shipping-labels-{count}.pdf");
})
.WithName("DownloadMockShippingLabelsPdf")
.WithSummary("Download the mock shipping labels PDF directly")
.WithDescription("count: number of labels/pages, >= 1 (default 1). pageSize: a QuestPDF size name, e.g. A4, A5, A3, Letter, Legal, Ledger (default: this API's ISO C7 114x81mm landscape label).")
.Produces(StatusCodes.Status200OK, contentType: "application/pdf")
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/shipping-labels/preview - build the mock shipping labels PDF and push it to the Companion App
app.MapPost("/api/mock/shipping-labels/preview", (ShippingLabelsPreviewRequest request, IMockShippingLabelsSource source, IShippingLabelPdfService pdf, IDocumentDeliveryService delivery) =>
{
    if (request.Count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    if (!PageSizeResolver.TryResolve(request.PageSize, PageOrientation.Landscape, out var resolvedPageSize, out var pageSizeError))
        return Results.BadRequest(new { error = pageSizeError });

    var labels = source.GenerateLabels(request.Count);
    var document = pdf.BuildLabelsDocument(labels, resolvedPageSize);
    return delivery.Preview(document, request.CompanionPort, $"shipping labels ({labels.Count} label(s))");
})
.WithName("PreviewMockShippingLabels")
.WithSummary("Preview the mock shipping labels in the Companion App")
.WithDescription("count: number of labels/pages, >= 1 (default 1). companionPort: Companion App port (default 12500). pageSize: a QuestPDF size name, e.g. A4, A5, A3, Letter, Legal, Ledger (default: this API's ISO C7 114x81mm landscape label).")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/shipping-labels/print - build the mock shipping labels PDF and send it straight to a printer.
// pageSize/orientation/fitMode are print-only here: they're forwarded as-is to SumatraPDF
// (see PrinterService.PrintFileAsync) and never change the PDF's own page geometry, which is
// always built at PageSizeResolver.BuildDefault(). That also means pageSize isn't limited to
// PageSizeResolver's named sizes - any "paper=" value SumatraPDF/the driver accepts works.
app.MapPost("/api/mock/shipping-labels/print", async (ShippingLabelsPrintRequest request, IMockShippingLabelsSource source, IShippingLabelPdfService pdf, IDocumentDeliveryService delivery, CancellationToken ct) =>
{
    if (request.Count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var labels = source.GenerateLabels(request.Count);
    var document = pdf.BuildLabelsDocument(labels, PageSizeResolver.BuildDefault());
    return await delivery.PrintAsync(document, request.PrinterName, $"shipping labels ({labels.Count} label(s))", request.Dpi, request.FitMode, request.Orientation, request.PageSize, ct);
})
.WithName("PrintMockShippingLabels")
.WithSummary("Print the mock shipping labels")
.WithDescription("printerName: required, see GET /api/printers. count: number of labels/pages, >= 1 (default 1). dpi: optional, omit for the printer's own default resolution. pageSize, orientation, fitMode are print-only - forwarded to SumatraPDF, not used to resize the PDF: pageSize is any SumatraPDF paper value (e.g. A4, or a custom size like '76mm x 130mm'), omit for the printer's current paper; orientation is Landscape (default) or Portrait; fitMode is Fit (default, no rescale) or Contain (scale to fit).")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

app.Run();