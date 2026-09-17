using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuestPdfPrinterApi.Services;

public interface IPrinterService
{
    Task<List<string>> GetAvailablePrintersAsync(CancellationToken ct = default);
    Task PrintFileAsync(string filePath, string printerName, CancellationToken ct = default);
}

/// <summary>
/// Picks a platform-specific printer implementation at resolve time.
/// QuestPDF only generates PDF bytes - actually reaching a physical/virtual
/// printer is always an OS-level call, so this is unavoidable either way.
/// </summary>
public class PrinterService : IPrinterService
{
    private readonly IPrinterService _inner;

    public PrinterService(IConfiguration config)
    {
        _inner = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsPrinterService(config)
            : new LinuxPrinterService(config);
    }

    public Task<List<string>> GetAvailablePrintersAsync(CancellationToken ct = default)
        => _inner.GetAvailablePrintersAsync(ct);

    public Task PrintFileAsync(string filePath, string printerName, CancellationToken ct = default)
        => _inner.PrintFileAsync(filePath, printerName, ct);
}

/// <summary>
/// Windows: lists printers via PowerShell's Get-Printer, and prints via SumatraPDF's
/// silent command-line printing (-print-to). SumatraPDF is free/portable; download the
/// portable build (SumatraPDF-x.x-64.zip from sumatrapdf.com/download-free-pdf-viewer)
/// and drop SumatraPDF.exe into the project's Tools/SumatraPDF folder. The .csproj copies
/// that folder next to the app's own binaries on every build, and this class resolves a
/// relative "Printing:SumatraPdfPath" against the app's base directory (not the current
/// working directory), so it's found the same way whether you `dotnet run` or double-click
/// the .exe in bin/Debug/net8.0. If you'd rather not take that dependency, ShellExecute's
/// "printto" verb is a fallback but needs a PDF handler (Adobe Reader, etc.) registered to
/// support it - less reliable for unattended/service use.
///
/// DPI: SumatraPDF's command line has no per-job resolution flag - it always rasterizes
/// at whatever the printer driver's *current default* is (see -print-settings docs; only
/// scale/fit/paper/collate/duplex tokens exist, nothing for resolution). So raising DPI
/// means changing that persistent driver default before the job goes out, via the
/// Win32_PrinterConfiguration WMI class (XResolution/YResolution/PrintQuality). This is
/// the same thing Devices and Printers -> Printer Properties -> Printing Preferences ->
/// Advanced -> "Print Quality" changes, just scripted. Two caveats worth knowing:
///  - It's a persistent queue setting, not scoped to this one job - it stays in effect
///    for whatever prints next (from any app) until changed again.
///  - Not every driver exposes raw XResolution/YResolution as writable; some only accept
///    a fixed PrintQuality enum (-4=high/-3=medium/-2=low/-1=draft) and ignore literal DPI
///    numbers, or reject the WMI Put() outright. Treat this as best-effort: log and keep
///    printing at whatever resolution the driver ends up with rather than failing the job.
/// </summary>
public class WindowsPrinterService : IPrinterService
{
    private readonly string _sumatraPath;
    private readonly int? _dpiOverride;

    public WindowsPrinterService(IConfiguration config)
    {
        var sumatraPath = config["Printing:SumatraPdfPath"];
        var configuredPath = string.IsNullOrWhiteSpace(sumatraPath)
            ? Path.Combine("Tools", "SumatraPDF", "SumatraPDF.exe")
            : sumatraPath;

        _sumatraPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

        _dpiOverride = config.GetValue<int?>("Printing:DpiOverride");
    }

    public async Task<List<string>> GetAvailablePrintersAsync(CancellationToken ct = default)
    {
        var output = await RunAsync("powershell", "-NoProfile -Command \"Get-Printer | Select-Object -ExpandProperty Name\"", ct);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public async Task PrintFileAsync(string filePath, string printerName, CancellationToken ct = default)
    {
        if (!File.Exists(_sumatraPath))
        {
            throw new FileNotFoundException(
                $"SumatraPDF.exe not found at '{_sumatraPath}'. Download the portable build and set " +
                "\"Printing:SumatraPdfPath\" in appsettings.json, or swap in your own print mechanism.",
                _sumatraPath);
        }

        if (_dpiOverride is int dpi)
            await TrySetDpiAsync(printerName, dpi, ct);

        var args = $"-print-to \"{printerName}\" -silent \"{filePath}\"";
        await RunAsync(_sumatraPath, args, ct);
    }

    /// <summary>
    /// Best-effort: raises the printer's default resolution via WMI so the print job that
    /// follows picks it up. Failures are swallowed (not thrown) because an unsupported
    /// driver shouldn't block the label from printing at whatever DPI it does support -
    /// see the class remarks above for why this can't just be a SumatraPDF CLI flag.
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
            // fixed PrintQuality enum), or Put() may be rejected outright. Either way, fall
            // through and print at whatever resolution the driver currently has rather than
            // failing the job over a DPI setting.
            Console.Error.WriteLine($"Could not set DPI to {dpi} for printer '{printerName}': {ex.Message}");
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

/// <summary>
/// Linux/macOS: lists and prints through CUPS (lpstat / lp), which is what almost every
/// Linux/macOS box already uses for printing, network or local.
///
/// DPI: unlike SumatraPDF, `lp` supports resolution as a genuine per-job option via
/// -o Resolution=NNNdpi, so this is a straight command-line flag - no persistent
/// queue-default hack needed. The catch is that CUPS only honors it if the printer's PPD
/// actually declares a "Resolution" option with that value; if it doesn't, CUPS silently
/// ignores the option instead of failing the job (check with `lpoptions -p &lt;printer&gt; -l`
/// to see the driver's supported values before assuming a given DPI took effect).
/// </summary>
public class LinuxPrinterService : IPrinterService
{
    private readonly int? _dpiOverride;

    public LinuxPrinterService(IConfiguration config)
    {
        _dpiOverride = config.GetValue<int?>("Printing:DpiOverride");
    }

    public async Task<List<string>> GetAvailablePrintersAsync(CancellationToken ct = default)
    {
        var output = await RunAsync("lpstat", "-p", ct);
        // Lines look like: "printer Office_LaserJet is idle. enabled since ..."
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("printer "))
            .Select(l => l.Split(' ')[1])
            .ToList();
    }

    public async Task PrintFileAsync(string filePath, string printerName, CancellationToken ct = default)
    {
        var resolutionArg = _dpiOverride is int dpi ? $"-o Resolution={dpi}dpi " : "";
        await RunAsync("lp", $"-d {printerName} {resolutionArg}\"{filePath}\"", ct);
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

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'{fileName} {arguments}' exited {process.ExitCode}: {stderr}");

        return stdout;
    }
}