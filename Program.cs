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
// label is one page, so count == page count for the PDF endpoints below. PageSize/
// Orientation are resolved once here via PageSizeResolver and handed to the PDF service
// as a concrete PageSize - see its own remarks for why (the default page size is this
// API's 110mm x 84mm label, not A4).

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

// GET /api/mock/shipping-labels/pdf?count=1&pageSize=A4&orientation=Portrait - generates
// mock labels in-process and turns them straight into label pages (one label per page)
app.MapGet("/api/mock/shipping-labels/pdf", (IMockShippingLabelsSource source, IShippingLabelPdfService pdf, int count = 1, string? pageSize = null, PageOrientation orientation = PageOrientation.Landscape) =>
{
    if (count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    if (!PageSizeResolver.TryResolve(pageSize, orientation, out var resolvedPageSize, out var pageSizeError))
        return Results.BadRequest(new { error = pageSizeError });

    var labels = source.GenerateLabels(count);

    var document = pdf.BuildLabelsDocument(labels, resolvedPageSize);
    var bytes = document.GeneratePdf();
    return Results.File(bytes, "application/pdf", $"shipping-labels-{count}.pdf");
})
.WithName("DownloadMockShippingLabelsPdf")
.WithSummary("Download the mock shipping labels PDF directly")
.WithDescription("Generates mock shipping-label data in-process and renders it as one page per label. count controls how many labels (pages); pageSize/orientation control the page geometry (defaults to this API's 110mm x 84mm landscape label - pass e.g. pageSize=A4 for a full sheet, or orientation=Portrait to swap width/height).")
.Produces(StatusCodes.Status200OK, contentType: "application/pdf")
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/shipping-labels/preview - build the mock shipping labels PDF and push it to the Companion App
app.MapPost("/api/mock/shipping-labels/preview", (ShippingLabelsPreviewRequest request, IMockShippingLabelsSource source, IShippingLabelPdfService pdf, IDocumentDeliveryService delivery) =>
{
    if (request.Count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    if (!PageSizeResolver.TryResolve(request.PageSize, request.Orientation, out var resolvedPageSize, out var pageSizeError))
        return Results.BadRequest(new { error = pageSizeError });

    var labels = source.GenerateLabels(request.Count);
    var document = pdf.BuildLabelsDocument(labels, resolvedPageSize);
    return delivery.Preview(document, request.CompanionPort, $"shipping labels ({labels.Count} label(s))");
})
.WithName("PreviewMockShippingLabels")
.WithSummary("Preview the mock shipping labels in the Companion App")
.WithDescription("Generates mock shipping-label data in-process, builds the PDF, and pushes it to a running Companion App instance. count controls how many labels (pages); pageSize/orientation control the page geometry as above.")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/shipping-labels/print - build the mock shipping labels PDF and send it straight to a printer
app.MapPost("/api/mock/shipping-labels/print", async (ShippingLabelsPrintRequest request, IMockShippingLabelsSource source, IShippingLabelPdfService pdf, IDocumentDeliveryService delivery, CancellationToken ct) =>
{
    if (request.Count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    if (!PageSizeResolver.TryResolve(request.PageSize, request.Orientation, out var resolvedPageSize, out var pageSizeError))
        return Results.BadRequest(new { error = pageSizeError });

    // Derived from the PDF's own resolved geometry rather than echoing request.Orientation
    // directly, so the "-print-settings" orientation token sent to the printer can never
    // drift out of sync with how the page was actually built - see
    // PrinterService.PrintFileAsync's remarks.
    var resolvedOrientation = resolvedPageSize.Width > resolvedPageSize.Height ? PageOrientation.Landscape : PageOrientation.Portrait;

    var labels = source.GenerateLabels(request.Count);
    var document = pdf.BuildLabelsDocument(labels, resolvedPageSize);
    return await delivery.PrintAsync(document, request.PrinterName, $"shipping labels ({labels.Count} label(s))", request.Dpi, request.FitMode, resolvedOrientation, ct);
})
.WithName("PrintMockShippingLabels")
.WithSummary("Print the mock shipping labels")
.WithDescription("Generates mock shipping-label data in-process, builds the PDF, and sends it to the given printer. count controls how many labels (pages). Use GET /api/printers for valid printerName values. Dpi is optional - omit it (recommended for Bluetooth/thermal label printers) to print at the printer's own current resolution instead of forcing one. fitMode controls how the driver reconciles the PDF against its configured paper size (Fit = no rescale, the default; Contain = scale to fit, preserving aspect ratio) - see PrintFitMode.")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

app.Run();