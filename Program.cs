using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;
using QuestPdfPrinterApi.Services;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IMockShipmentsSource, MockShipmentsSource>();
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
        Description = "Builds 4x6in shipping labels with QuestPDF from mock shipment data only. " +
                      "Download, preview, and print are all backed by the mock shipments source - preview and print each have their own endpoint."
    });
});

var app = builder.Build();

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
    var names = await printers.GetAvailablePrintersAsync(ct);
    return Results.Ok(names);
})
.WithName("GetPrinters")
.WithSummary("List available printers")
.WithDescription("Returns the printer names known to the OS (Get-Printer on Windows, lpstat -p on Linux/macOS).")
.Produces<List<string>>(StatusCodes.Status200OK);

// ---- Mock shipments API (self-hosted stand-in "external" data source) ----
// count controls the array length: count=1 -> 1 object, count=10 -> 10 objects. Each
// shipment is one 4x6in label page, so count == page count for the PDF endpoints below.

// GET /api/mock/shipments?count=1 - the mock API route itself
app.MapGet("/api/mock/shipments", (IMockShipmentsSource source, int count = 1) =>
{
    if (count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var shipments = source.GenerateShipments(count);
    return Results.Ok(shipments);
})
.WithName("GetMockShipments")
.WithSummary("Mock shipments API")
.WithDescription("Stand-in for an external API. Returns a JSON array of randomly generated shipments - count=1 gives a 1-item array, count=10 gives a 10-item array, and so on.")
.Produces<List<MockShipment>>(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// GET /api/mock/shipments/pdf?count=1 - generates mock shipments in-process and turns
// them straight into 4x6in shipping label pages (one label per shipment)
app.MapGet("/api/mock/shipments/pdf", (IMockShipmentsSource source, IShippingLabelPdfService pdf, int count = 1) =>
{
    if (count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var shipments = source.GenerateShipments(count);

    var document = pdf.BuildLabelsDocument(shipments);
    var bytes = document.GeneratePdf();
    return Results.File(bytes, "application/pdf", $"shipping-labels-{count}.pdf");
})
.WithName("DownloadMockShipmentLabelsPdf")
.WithSummary("Download the mock shipping labels PDF directly")
.WithDescription("Generates mock shipment data in-process and renders it as one 4x6in label per shipment. count controls how many labels (pages).")
.Produces(StatusCodes.Status200OK, contentType: "application/pdf")
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/shipments/preview - build the mock shipping labels PDF and push it to the Companion App
app.MapPost("/api/mock/shipments/preview", (MockShipmentsPreviewRequest request, IMockShipmentsSource source, IShippingLabelPdfService pdf, IDocumentDeliveryService delivery) =>
{
    if (request.Count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var shipments = source.GenerateShipments(request.Count);
    var document = pdf.BuildLabelsDocument(shipments);
    return delivery.Preview(document, request.CompanionPort, $"shipping labels ({shipments.Count} label(s))");
})
.WithName("PreviewMockShipmentLabels")
.WithSummary("Preview the mock shipping labels in the Companion App")
.WithDescription("Generates mock shipment data in-process, builds the PDF, and pushes it to a running Companion App instance. count controls how many labels (pages).")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/shipments/print - build the mock shipping labels PDF and send it straight to a printer
app.MapPost("/api/mock/shipments/print", async (MockShipmentsPrintRequest request, IMockShipmentsSource source, IShippingLabelPdfService pdf, IDocumentDeliveryService delivery, CancellationToken ct) =>
{
    if (request.Count < 1)
        return Results.BadRequest(new { error = "count must be 1 or greater." });

    var shipments = source.GenerateShipments(request.Count);
    var document = pdf.BuildLabelsDocument(shipments);
    return await delivery.PrintAsync(document, request.PrinterName, $"shipping labels ({shipments.Count} label(s))", ct);
})
.WithName("PrintMockShipmentLabels")
.WithSummary("Print the mock shipping labels")
.WithDescription("Generates mock shipment data in-process, builds the PDF, and sends it to the given printer. count controls how many labels (pages). Use GET /api/printers for valid printerName values.")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

app.Run();
