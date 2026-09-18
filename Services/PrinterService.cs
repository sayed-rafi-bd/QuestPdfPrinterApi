using System.Diagnostics;
using System.Text.Json;
using QuestPdfPrinterApi.Models;

namespace QuestPdfPrinterApi.Services;

public interface IPrinterService
{
    Task<List<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken ct = default);
    Task PrintFileAsync(string filePath, string printerName, int? dpiOverride, CancellationToken ct = default);
}

/// <summary>
/// Windows-only. Lists printers via PowerShell's Get-Printer, and prints via SumatraPDF's
/// silent command-line printing (-print-to). SumatraPDF is free/portable; download the
/// portable build (SumatraPDF-x.x-64.zip from sumatrapdf.com/download-free-pdf-viewer) and
/// drop SumatraPDF.exe into the project's Tools/SumatraPDF folder. The .csproj copies that
/// folder next to the app's own binaries on every build, and this class resolves a
/// relative "Printing:SumatraPdfPath" against the app's base directory (not the current
/// working directory), so it's found the same way whether you `dotnet run` or double-click
/// the .exe in bin/Debug/net8.0. If you'd rather not take that dependency, ShellExecute's
/// "printto" verb is a fallback but needs a PDF handler (Adobe Reader, etc.) registered to
/// support it - less reliable for unattended/service use.
///
/// PRINTER SUPPORT: Get-Printer returns every printer Windows itself has a queue for,
/// regardless of connection type - USB, network/IP, AirPrint/IPP, and Bluetooth are all
/// just "a printer" to the spooler once installed. So a Bluetooth label printer works here
/// the same way any other printer does, as soon as it's been paired/added under Settings ->
/// Bluetooth & devices -> Printers & scanners; nothing in this class special-cases (or
/// blocks) any particular connection type. GetAvailablePrintersAsync also returns each
/// printer's PortName so a caller can tell queues apart when a machine has several - see
/// PrinterInfo's remarks for how (and how reliably) IsBluetooth is detected from that.
///
/// DPI: SumatraPDF's command line has no per-job resolution flag - it always rasterizes
/// at whatever the printer driver's *current default* is (see -print-settings docs; only
/// scale/fit/paper/collate/duplex tokens exist, nothing for resolution). So raising DPI
/// means changing that persistent driver default before the job goes out, via the
/// Win32_PrinterConfiguration WMI class (XResolution/YResolution/PrintQuality). This is
/// the same thing Devices and Printers -> Printer Properties -> Printing Preferences ->
/// Advanced -> "Print Quality" changes, just scripted. Three caveats worth knowing:
///  - It's a persistent queue setting, not scoped to this one job - it stays in effect
///    for whatever prints next (from any app) until changed again.
///  - Not every driver exposes raw XResolution/YResolution as writable; some only accept
///    a fixed PrintQuality enum (-4=high/-3=medium/-2=low/-1=draft) and ignore literal DPI
///    numbers, or reject the WMI Put() outright. Treat this as best-effort: log and keep
///    printing at whatever resolution the driver ends up with rather than failing the job.
///  - There is no single DPI that's right for every printer. Most Bluetooth/thermal label
///    printers only support one or two fixed native resolutions (commonly 203, 300, or 600
///    dpi) - forcing an unrelated value (e.g. a laser-printer-oriented 600 dpi) onto every
///    job regardless of which printer it's going to is a common cause of *worse* output on
///    those printers: the driver either rejects it and silently falls back to whatever
///    "draft" state it was last left in, or accepts it and the head ends up rasterizing at
///    a resolution it doesn't actually support, which shows up as soft/blurry text and
///    barcodes. That's why dpiOverride here is a per-job, opt-in parameter rather than a
///    single global setting applied to everything - callers should pass the value that
///    matches whatever printer they're targeting, and omit it entirely (recommended default
///    for label printers) to leave the driver's own current setting alone.
///
/// This class produces the same visible symptom described above regardless of DPI setting:
/// SumatraPDF -print-to rasterizes each page to a bitmap before it reaches the print
/// spooler, so what actually reaches the printer is a raster image, not a PDF/PostScript
/// pass-through. Raising Printing:DpiOverride makes that raster step happen at a higher
/// resolution (visually closer to vector output) but doesn't eliminate the rasterization
/// itself. See PrintFileAsync's remarks for what a true vector pass-through would require,
/// and for the separate "noscale" fix for blur caused by page-fit rescaling rather than DPI.
/// </summary>
public class PrinterService : IPrinterService
{
    private readonly string _sumatraPath;
    private readonly int? _defaultDpiOverride;

    public PrinterService(IConfiguration config)
    {
        var sumatraPath = config["Printing:SumatraPdfPath"];
        var configuredPath = string.IsNullOrWhiteSpace(sumatraPath)
            ? Path.Combine("Tools", "SumatraPDF", "SumatraPDF.exe")
            : sumatraPath;

        _sumatraPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

        // Fallback only - used when a print request doesn't specify its own dpiOverride.
        // Leave "Printing:DpiOverride" unset in appsettings.json unless every printer this
        // server ever prints to shares one native resolution; otherwise prefer passing
        // dpiOverride per request (see class remarks).
        _defaultDpiOverride = config.GetValue<int?>("Printing:DpiOverride");
    }

    public async Task<List<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken ct = default)
    {
        // ConvertTo-Json (rather than -ExpandProperty Name) so we get PortName alongside
        // Name without breaking on printer names that contain commas or newlines.
        var output = await RunAsync(
            "powershell",
            "-NoProfile -Command \"Get-Printer | Select-Object Name, PortName | ConvertTo-Json -Compress\"",
            ct);

        if (string.IsNullOrWhiteSpace(output))
            return new List<PrinterInfo>();

        // A single printer serializes as a JSON object rather than an array - normalize
        // both shapes before parsing.
        var trimmed = output.Trim();
        var json = trimmed.StartsWith('[') ? trimmed : $"[{trimmed}]";

        using var doc = JsonDocument.Parse(json);
        var result = new List<PrinterInfo>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var name = element.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? "" : "";
            var portName = element.TryGetProperty("PortName", out var portProp) ? portProp.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var isBluetooth = portName.Contains("BTH", StringComparison.OrdinalIgnoreCase)
                || portName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

            result.Add(new PrinterInfo(name, portName, isBluetooth));
        }

        return result;
    }

    /// <summary>
    /// Sends the file to the printer via SumatraPDF's silent CLI printing. This rasterizes
    /// the page (see class remarks) - it is NOT a raw PDF/PostScript pass-through to the
    /// driver. If your printer and driver genuinely support PostScript/PDF natively and you
    /// want to skip GDI rasterization entirely, the alternatives are: (a) spool the PDF
    /// bytes directly with the RAW datatype via the Win32 spooler APIs
    /// (OpenPrinter/StartDocPrinter/WritePrinter with pDatatype="RAW"), which only works if
    /// the printer/driver combo accepts PDF or PS as raw data - most consumer inkjet/laser
    /// drivers do not; or (b) render with a PDF library that draws vector primitives
    /// (paths/text/glyphs) onto a System.Drawing.Printing.PrintDocument's Graphics context
    /// at the printer's own native resolution instead of pre-rasterizing to a fixed-DPI
    /// bitmap. Both are a materially different print pipeline from this one - say the word
    /// if you want either implemented instead of/alongside SumatraPDF.
    ///
    /// "-print-settings noscale" is passed on every job. Without it, SumatraPDF's default
    /// behavior is to fit-to-page: rescale the PDF to match whatever paper size the printer
    /// driver is currently configured for. Every document this API builds is already
    /// generated at its intended physical page size (label stock size for labels, the
    /// requested pageSize for slips), so that rescale is pure downside here - if the
    /// driver's configured paper size doesn't match to the pixel, the extra resample step
    /// softens text and, worse, barcodes, which is a common source of "print quality is
    /// bad" complaints independent of DPI. noscale prints the page at 1:1 instead, so
    /// output stays as sharp as the chosen DPI actually allows. The tradeoff: if the
    /// printer's configured paper size is genuinely wrong for the document (e.g. printing a
    /// label to a queue still configured for Letter), noscale will make that mismatch
    /// visible (offset/clipped) rather than silently papering over it with a blurry rescale
    /// - which is the right failure mode, since it points at the actual misconfiguration.
    /// </summary>
    public async Task PrintFileAsync(string filePath, string printerName, int? dpiOverride, CancellationToken ct = default)
    {
        if (!File.Exists(_sumatraPath))
        {
            throw new FileNotFoundException(
                $"SumatraPDF.exe not found at '{_sumatraPath}'. Download the portable build and set " +
                "\"Printing:SumatraPdfPath\" in appsettings.json, or swap in your own print mechanism.",
                _sumatraPath);
        }

        var dpi = dpiOverride ?? _defaultDpiOverride;
        if (dpi is int resolvedDpi)
            await TrySetDpiAsync(printerName, resolvedDpi, ct);

        var args = $"-print-to \"{printerName}\" -print-settings \"noscale\" -silent \"{filePath}\"";
        await RunAsync(_sumatraPath, args, ct);
    }

    /// <summary>
    /// Best-effort: raises the printer's default resolution via WMI so the print job that
    /// follows picks it up. Failures are swallowed (not thrown) because an unsupported
    /// driver shouldn't block the label from printing at whatever DPI it does support -
    /// see the class remarks above for why this can't just be a SumatraPDF CLI flag, and
    /// for why the caller (not this method) is responsible for picking a DPI the target
    /// printer can actually do.
    /// </summary>
    private static async Task TrySetDpiAsync(string printerName, int dpi, CancellationToken ct)
    {
        // Single-quoted WMI filter value, so escape embedded single quotes by doubling them.
        var escapedName = printerName.Replace("'", "''");
        var script =
            $"$cfg = Get-WmiObject -Class Win32_PrinterConfiguration -Filter \"Name='{escapedName}'\"; " +
            "if ($null -eq $cfg) { throw \"No Win32_PrinterConfiguration found for printer '" + escapedName + "'\" }; " +
            $"$cfg.XResolution = {dpi}; $cfg.YResolution = {dpi}; $cfg.PrintQuality = {dpi}; $cfg.Put() | Out-Null";

        try
        {
            await RunAsync("powershell", $"-NoProfile -Command \"{script}\"", ct);
        }
        catch (Exception ex)
        {
            // Driver may not expose writable XResolution/YResolution (some only accept the
            // fixed PrintQuality enum), or Put() may be rejected outright - this is common
            // on thermal/label printer drivers, which often only support their one or two
            // native resolutions and reject anything else. Either way, fall through and
            // print at whatever resolution the driver currently has rather than failing the
            // job over a DPI setting.
            Console.Error.WriteLine(
                $"Could not set DPI to {dpi} for printer '{printerName}': {ex.Message}. If this is a " +
                "Bluetooth/thermal label printer, check its native resolution (commonly 203, 300, or " +
                "600 dpi) and pass that instead.");
        }
    }

    private static async Task<string> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'");

        // Read both streams concurrently rather than sequentially, so a full buffer on
        // one doesn't stall behind the other while the process is still writing to it.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(ct);

        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'{fileName} {arguments}' exited {process.ExitCode}: {stderr}");

        return stdout;
    }
}
