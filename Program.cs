using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;
using QuestPdfPrinterApi.Services;

QuestPDF.Settings.License = LicenseType.Community;

// ---- Installed-app behavior (see installer/) ----
// --open-browser : open the UI in the default browser once the server is up.
// --log-to-file  : write console output to %LocalAppData%\LabelApp\app.log (the installed
//                  build has no console window, so this is where errors/timings go).
// Only one instance runs per user session; launching it again just opens the browser at the
// running instance's URL. The mutex name must match AppMutex in installer/LabelApp.iss.
var openBrowser = args.Contains("--open-browser");
var logToFile = args.Contains("--log-to-file");
var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelApp");
Directory.CreateDirectory(stateDir);
var urlFile = Path.Combine(stateDir, "url.txt");

using var instanceMutex = new Mutex(true, "LabelApp.SingleInstance", out var isFirstInstance);
if (!isFirstInstance)
{
    if (openBrowser)
    {
        // The first instance may still be starting - wait up to ~10s for it to publish its URL.
        for (var i = 0; i < 40; i++)
        {
            if (File.Exists(urlFile))
            {
                try { OpenBrowser(File.ReadAllText(urlFile).Trim()); } catch { /* ignore */ }
                break;
            }
            Thread.Sleep(250);
        }
    }
    return;
}

try { File.Delete(urlFile); } catch { /* stale file from a crashed run - overwritten on start */ }

if (logToFile)
{
    var logPath = Path.Combine(stateDir, "app.log");
    try
    {
        if (File.Exists(logPath) && new FileInfo(logPath).Length > 2_000_000)
            File.Delete(logPath);
        var writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);
    }
    catch { /* logging is best-effort */ }
}

var builder = WebApplication.CreateBuilder(args);

// Network access (phones/tablets on the same Wi-Fi): listen on all network adapters instead of
// localhost only. On when ANY of: the --network argument, a "network.enabled" marker file next to
// the exe (the installer's "Allow other devices" option creates it), or "App:AllowNetworkAccess"
// in appsettings.json. There is no login, so only enable it on a network you trust.
var allowNetwork = args.Contains("--network")
    || File.Exists(Path.Combine(AppContext.BaseDirectory, "network.enabled"))
    || builder.Configuration.GetValue<bool>("App:AllowNetworkAccess");

// No explicit URL (--urls / ASPNETCORE_URLS, which `dotnet run` sets from launchSettings.json):
// listen on 5080 or the next free port if something else is using it - on localhost only,
// unless network access is enabled above.
var hasExplicitUrls = args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_URLS"));
if (!hasExplicitUrls)
{
    var bindAddress = allowNetwork ? IPAddress.Any : IPAddress.Loopback;
    var host = allowNetwork ? "0.0.0.0" : "localhost";
    builder.WebHost.UseUrls($"http://{host}:{FindFreePort(5080, bindAddress)}");
}

// So PageOrientation is accepted/returned as "Portrait"/"Landscape" in
// JSON request/response bodies instead of raw integers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<IMockShippingLabelsSource, MockShippingLabelsSource>();
builder.Services.AddSingleton<IShippingLabelPdfService, ShippingLabelPdfService>();
builder.Services.AddSingleton<IPrinterService, PrinterService>();
builder.Services.AddSingleton<IDocumentDeliveryService, DocumentDeliveryService>();

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = (app.Urls.FirstOrDefault() ?? "http://localhost:5080")
        .Replace("://0.0.0.0", "://localhost")
        .Replace("://[::]", "://localhost")
        .Replace("://*", "://localhost")
        .Replace("://+", "://localhost");
    try { File.WriteAllText(urlFile, url); } catch { /* best-effort */ }
    Console.WriteLine($"[APP] Listening on {url}");
    if (openBrowser)
        OpenBrowser(url);
});
app.Lifetime.ApplicationStopping.Register(() =>
{
    try { File.Delete(urlFile); } catch { /* best-effort */ }
});

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

// GET /api/server-info - tells the UI which addresses this app can be opened from, so it can
// show the address to type on a phone/tablet. Network URLs are only listed when the app is
// actually listening on all adapters (see allowNetwork above, or an explicit --urls with
// 0.0.0.0 / *). These are LAN addresses - reachable from the same network, not the internet.
app.MapGet("/api/server-info", (HttpContext ctx) =>
{
    var port = ctx.Request.Host.Port ?? 80;
    var networkEnabled = app.Urls.Any(u =>
        u.Contains("://0.0.0.0") || u.Contains("://[::]") || u.Contains("://*") || u.Contains("://+"));
    var networkUrls = networkEnabled
        ? GetLanAddresses().Select(ip => $"http://{ip}:{port}").ToList()
        : new List<string>();
    return Results.Ok(new
    {
        localUrl = $"http://localhost:{port}",
        networkAccessEnabled = networkEnabled,
        networkUrls
    });
})
.WithName("GetServerInfo")
.Produces(StatusCodes.Status200OK);

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
// fit-to-paper ("fit"), so there is no fit mode to choose.
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
.WithDescription("printerName: required, see GET /api/printers. count: number of labels/pages, >= 1 (default 1). dpi: optional, omit for the printer's own default resolution. pageSize and orientation are print-only - forwarded to SumatraPDF, not used to resize the PDF: pageSize is any SumatraPDF paper value (e.g. A4, or a custom size like '76mm x 130mm'), omit for the printer's current paper; orientation is Landscape (default) or Portrait. Printing always scales the page to fit the paper.")
.Produces(StatusCodes.Status200OK)
.ProducesValidationProblem(StatusCodes.Status400BadRequest);

app.Run();

static int FindFreePort(int start, IPAddress address)
{
    for (var port = start; port < start + 50; port++)
    {
        try
        {
            var listener = new TcpListener(address, port);
            listener.Start();
            listener.Stop();
            return port;
        }
        catch (SocketException) { /* in use - try the next one */ }
    }
    throw new InvalidOperationException($"No free port found between {start} and {start + 49}.");
}

static void OpenBrowser(string url)
{
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch (Exception ex) { Console.Error.WriteLine($"[APP] Could not open the browser for {url}: {ex.Message}"); }
}

// IPv4 addresses of real LAN adapters (up, not loopback, has a default gateway - which skips
// most VM/virtual-switch adapters - and not link-local 169.254.x.x). Wi-Fi/Ethernet first.
static List<string> GetLanAddresses()
{
    var result = new List<string>();
    try
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            var props = nic.GetIPProperties();
            if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                 && !g.Address.Equals(IPAddress.Any))) continue;

            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = ua.Address.ToString();
                if (ip.StartsWith("169.254.")) continue;
                if (!result.Contains(ip)) result.Add(ip);
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[APP] Could not list network addresses: {ex.Message}");
    }
    return result;
}
