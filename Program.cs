using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;
using QuestPdfPrinterApi.Services;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IMockPickingSlipsSource, MockPickingSlipsSource>();
builder.Services.AddSingleton<IPickingSlipPdfService, PickingSlipPdfService>();
builder.Services.AddSingleton<IPrinterService, PrinterService>();
builder.Services.AddSingleton<IDocumentDeliveryService, DocumentDeliveryService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "QuestPDF Printer API",
        Version = "v1",
        Description = "Builds Japanese picking/inspection slips (検品書) with QuestPDF, from mock data " +
                      "only. Download, preview, and print are all backed by their respective mock " +
                      "sources - preview and print each have their own endpoint."
    });
});

var app = builder.Build();

// Bundle a CJK font the same way PrinterService bundles SumatraPDF.exe: drop the
// .ttf/.otf in Tools/Fonts (copied next to the app's binaries by the .csproj), point
// "Fonts:NotoSansJpPath" at it if the location differs. PickingSlipPdfService's Japanese
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
        $"[FONT WARNING] '{resolvedFontPath}' not found - picking slips (検品書) will render with " +
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

// ---- Mock picking slips API (self-hosted stand-in "external" data source) ----
// count controls the array length: count=1 -> 1 object, count=10 -> 10 objects. Each
// slip is one page, so count == page count for the PDF endpoints below.

// GET /api/mock/picking-slips?count=1 - the mock API route itself
app.MapGet("/api/mock/picking-slips", (IMockPickingSlipsSource source, int count = 1) =>
{
    if (count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var slips = source.GenerateSlips(count);
    return Results.Ok(slips);
})
.WithName("GetMockPickingSlips")
.WithSummary("Mock picking slips API")
.WithDescription("Stand-in for an external API. Returns a JSON array of randomly generated picking/inspection slips (検品書) - count=1 gives a 1-item array, count=10 gives a 10-item array, and so on.")
.Produces<List<PickingSlip>>(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// GET /api/mock/picking-slips/pdf?count=1&pageSize=A4 - generates mock slips in-process
// and turns them straight into picking-slip pages (one slip per page)
app.MapGet("/api/mock/picking-slips/pdf", (IMockPickingSlipsSource source, IPickingSlipPdfService pdf, int count = 1, string? pageSize = null) =>
{
    if (count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var slips = source.GenerateSlips(count);

    var document = pdf.BuildSlipsDocument(slips, pageSize);
    var bytes = document.GeneratePdf();
    return Results.File(bytes, "application/pdf", $"picking-slips-{count}.pdf");
})
.WithName("DownloadMockPickingSlipsPdf")
.WithSummary("Download the mock picking slips PDF directly")
.WithDescription("Generates mock picking-slip data in-process and renders it as one page per slip. count controls how many slips (pages); pageSize controls the paper size (A4, Letter, etc. - defaults to A4).")
.Produces(StatusCodes.Status200OK, contentType: "application/pdf")
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/picking-slips/preview - build the mock picking slips PDF and push it to the Companion App
app.MapPost("/api/mock/picking-slips/preview", (PickingSlipsPreviewRequest request, IMockPickingSlipsSource source, IPickingSlipPdfService pdf, IDocumentDeliveryService delivery) =>
{
    if (request.Count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var slips = source.GenerateSlips(request.Count);
    var document = pdf.BuildSlipsDocument(slips, request.PageSize);
    return delivery.Preview(document, request.CompanionPort, $"picking slips ({slips.Count} slip(s))");
})
.WithName("PreviewMockPickingSlips")
.WithSummary("Preview the mock picking slips in the Companion App")
.WithDescription("Generates mock picking-slip data in-process, builds the PDF, and pushes it to a running Companion App instance. count controls how many slips (pages).")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/picking-slips/print - build the mock picking slips PDF and send it straight to a printer
app.MapPost("/api/mock/picking-slips/print", async (PickingSlipsPrintRequest request, IMockPickingSlipsSource source, IPickingSlipPdfService pdf, IDocumentDeliveryService delivery, CancellationToken ct) =>
{
    if (request.Count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var slips = source.GenerateSlips(request.Count);
    var document = pdf.BuildSlipsDocument(slips, request.PageSize);
    return await delivery.PrintAsync(document, request.PrinterName, $"picking slips ({slips.Count} slip(s))", request.Dpi, ct);
})
.WithName("PrintMockPickingSlips")
.WithSummary("Print the mock picking slips")
.WithDescription("Generates mock picking-slip data in-process, builds the PDF, and sends it to the given printer. count controls how many slips (pages). Use GET /api/printers for valid printerName values. Dpi is optional - omit it (recommended for Bluetooth/thermal label printers) to print at the printer's own current resolution instead of forcing one.")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

app.Run();
