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
            ? new WindowsPrinterService(config["Printing:SumatraPdfPath"])
            : new LinuxPrinterService();
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
/// </summary>
public class WindowsPrinterService : IPrinterService
{
    private readonly string _sumatraPath;

    public WindowsPrinterService(string? sumatraPath)
    {
        var configuredPath = string.IsNullOrWhiteSpace(sumatraPath)
            ? Path.Combine("Tools", "SumatraPDF", "SumatraPDF.exe")
            : sumatraPath;

        _sumatraPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);
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

        var args = $"-print-to \"{printerName}\" -silent \"{filePath}\"";
        await RunAsync(_sumatraPath, args, ct);
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
/// </summary>
public class LinuxPrinterService : IPrinterService
{
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
        await RunAsync("lp", $"-d {printerName} \"{filePath}\"", ct);
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
