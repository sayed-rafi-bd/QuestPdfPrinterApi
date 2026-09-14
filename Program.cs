using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;
using QuestPdfPrinterApi.Services;

QuestPDF.Settings.License = LicenseType.Community;

const int RowsPerPage = 10;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IMockProductsSource, MockProductsSource>();
builder.Services.AddSingleton<IMockProductsPdfService, MockProductsPdfService>();
builder.Services.AddSingleton<IPrinterService, PrinterService>();
builder.Services.AddSingleton<IDocumentDeliveryService, DocumentDeliveryService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "QuestPDF Printer API",
        Version = "v1",
        Description = "Builds PDFs with QuestPDF from mock products data only. " +
                      "Download, preview, and print are all backed by the mock products source - preview and print each have their own endpoint."
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

// ---- Mock products API (self-hosted stand-in "external" data source) ----
// pages controls the array length: pages=1 -> 1 object, pages=10 -> 10 objects.

// GET /api/mock/products?pages=1 - the mock API route itself
app.MapGet("/api/mock/products", (IMockProductsSource source, int pages = 1) =>
{
    if (pages < 1)
        return Results.BadRequest(new { error = "pages must be 1 or greater." });

    var products = source.GenerateProducts(pages);
    return Results.Ok(products);
})
.WithName("GetMockProducts")
.WithSummary("Mock products API")
.WithDescription("Stand-in for an external API. Returns a JSON array of randomly generated products - pages=1 gives a 1-item array, pages=10 gives a 10-item array, and so on.")
.Produces<List<MockProduct>>(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// GET /api/mock/products/pdf?pages=1 - generates mock products in-process and turns them
// straight into a PDF table
app.MapGet("/api/mock/products/pdf", (IMockProductsSource source, IMockProductsPdfService pdf, string? pageSize, int pages = 1) =>
{
    if (!PageSizeResolver.TryResolve(pageSize, out var resolvedSize, out var sizeError))
        return Results.BadRequest(new { error = sizeError });

    if (pages < 1)
        return Results.BadRequest(new { error = "pages must be 1 or greater." });

    var products = source.GenerateProducts(pages * RowsPerPage);

    var document = pdf.BuildProductsDocument(products, resolvedSize);
    var bytes = document.GeneratePdf();
    return Results.File(bytes, "application/pdf", $"mock-products-{pages}.pdf");
})
.WithName("DownloadMockProductsPdf")
.WithSummary("Download the mock products PDF directly")
.WithDescription("Generates mock product data in-process and renders it as a PDF table. pages controls PDF pages (10 rows per page). Optional ?pageSize=Letter.")
.Produces(StatusCodes.Status200OK, contentType: "application/pdf")
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/products/preview - build the mock-products PDF and push it to the Companion App
app.MapPost("/api/mock/products/preview", (MockProductsPreviewRequest request, IMockProductsSource source, IMockProductsPdfService pdf, IDocumentDeliveryService delivery) =>
{
    if (!PageSizeResolver.TryResolve(request.PageSize, out var pageSize, out var sizeError))
        return Results.BadRequest(new { error = sizeError });

    if (request.Pages < 1)
        return Results.BadRequest(new { error = "pages must be 1 or greater." });

    var products = source.GenerateProducts(request.Pages * RowsPerPage);
    var document = pdf.BuildProductsDocument(products, pageSize);
    return delivery.Preview(document, request.CompanionPort, $"mock products report ({products.Count} item(s))");
})
.WithName("PreviewMockProducts")
.WithSummary("Preview the mock products report in the Companion App")
.WithDescription("Generates mock product data in-process, builds the PDF, and pushes it to a running Companion App instance. pages controls PDF pages (10 rows per page).")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

// POST /api/mock/products/print - build the mock-products PDF and send it straight to a printer
app.MapPost("/api/mock/products/print", async (MockProductsPrintRequest request, IMockProductsSource source, IMockProductsPdfService pdf, IDocumentDeliveryService delivery, CancellationToken ct) =>
{
    if (!PageSizeResolver.TryResolve(request.PageSize, out var pageSize, out var sizeError))
        return Results.BadRequest(new { error = sizeError });

    if (request.Pages < 1)
        return Results.BadRequest(new { error = "pages must be 1 or greater." });

    var products = source.GenerateProducts(request.Pages * RowsPerPage);
    var document = pdf.BuildProductsDocument(products, pageSize);
    return await delivery.PrintAsync(document, request.PrinterName, $"mock products report ({products.Count} item(s))", ct);
})
.WithName("PrintMockProducts")
.WithSummary("Print the mock products report")
.WithDescription("Generates mock product data in-process, builds the PDF, and sends it to the given printer. pages controls PDF pages (10 rows per page). Use GET /api/printers for valid printerName values.")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

app.Run();
