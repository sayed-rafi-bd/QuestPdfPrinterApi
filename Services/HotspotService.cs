using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuestPdfPrinterApi.Services;

/// <param name="Supported">False when not running on Windows (hotspot control is Windows-only).</param>
/// <param name="State">"off", "on", "starting" (in transition) or "unknown".</param>
/// <param name="ClientCount">Devices currently joined to the hotspot.</param>
/// <param name="WifiQrPayload">Standard "WIFI:T:WPA;S:...;P:...;;" text - phone cameras offer to join the network when they scan it.</param>
public record HotspotState(
    bool Supported,
    string State,
    int ClientCount,
    string? Ssid,
    string? Passphrase,
    string? WifiQrPayload,
    string? Error);

public interface IHotspotService
{
    Task<HotspotState> GetStateAsync(CancellationToken ct = default);
    Task<HotspotState> StartAsync(CancellationToken ct = default);
    Task<HotspotState> StopAsync(CancellationToken ct = default);

    /// <summary>Stops the hotspot on app shutdown, but only if this app is the one that started it.</summary>
    Task StopIfStartedByAppAsync();

    /// <summary>This PC's IPv4 address on the hotspot network (normally 192.168.137.1), or null if not found.</summary>
    string? GetHotspotAddress();
}

/// <summary>
/// Turns Windows' built-in Mobile Hotspot on/off and reports how many devices joined, through
/// the WinRT NetworkOperatorTetheringManager API - driven from Windows PowerShell, the same
/// way PrinterService drives Get-Printer, so the project doesn't need a Windows-specific
/// target framework.
///
/// Requirements/limits (all Windows': they come from Mobile Hotspot itself):
///  - Windows 10 or later, with a Wi-Fi adapter that supports hosting a hotspot.
///  - The PC needs some network connection to "share" (Windows won't create a hotspot with no
///    connection profile at all). The phone then just gets a Wi-Fi network with no internet.
///  - Starting the hotspot overwrites the SSID/password of Windows' own Mobile Hotspot settings
///    with this app's credentials (generated once, kept in %LocalAppData%\LabelApp\hotspot.json
///    so the QR code stays the same between runs).
/// </summary>
public class HotspotService : IHotspotService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _credentialsFile;
    private (string Ssid, string Passphrase)? _credentials;
    private volatile bool _startedByApp;

    public HotspotService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelApp");
        Directory.CreateDirectory(dir);
        _credentialsFile = Path.Combine(dir, "hotspot.json");
    }

    public Task<HotspotState> GetStateAsync(CancellationToken ct = default) => RunAsync("status", ct);

    public async Task<HotspotState> StartAsync(CancellationToken ct = default)
    {
        var state = await RunAsync("start", ct);
        if (state.Error is null && state.State == "on")
            _startedByApp = true;
        return state;
    }

    public async Task<HotspotState> StopAsync(CancellationToken ct = default)
    {
        var state = await RunAsync("stop", ct);
        if (state.Error is null)
            _startedByApp = false;
        return state;
    }

    public async Task StopIfStartedByAppAsync()
    {
        if (!_startedByApp) return;
        try { await StopAsync(); }
        catch (Exception ex) { Console.Error.WriteLine($"[HOTSPOT] Could not stop hotspot on shutdown: {ex.Message}"); }
    }

    public string? GetHotspotAddress()
    {
        try
        {
            string? fallback = null;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address.ToString();
                    // Windows' hotspot (Internet Connection Sharing) always uses 192.168.137.x.
                    if (ip.StartsWith("192.168.137.")) return ip;
                    if (nic.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase))
                        fallback ??= ip;
                }
            }
            return fallback;
        }
        catch { return null; }
    }

    private async Task<HotspotState> RunAsync(string action, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return new HotspotState(false, "off", 0, null, null, null, "Hotspot control needs Windows 10 or later.");

        await _gate.WaitAsync(ct);
        try
        {
            var (ssid, passphrase) = GetOrCreateCredentials();

            var psi = new ProcessStartInfo("powershell")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)));
            // Passed as environment variables (not spliced into the script) so nothing here can inject PowerShell.
            psi.Environment["HOTSPOT_ACTION"] = action;
            psi.Environment["HOTSPOT_SSID"] = ssid;
            psi.Environment["HOTSPOT_PASS"] = passphrase;

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start PowerShell.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(timeout.Token);

            // PowerShell may print other lines first; the script's own JSON is always the last line.
            var json = stdoutTask.Result
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            if (string.IsNullOrEmpty(json))
                return Failure($"PowerShell returned nothing. {stderrTask.Result.Trim()}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!(root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean()))
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : "Unknown hotspot error.";
                return Failure(error ?? "Unknown hotspot error.");
            }

            var stateCode = root.GetProperty("state").GetInt32();
            var clients = root.GetProperty("clients").GetInt32();
            var state = stateCode switch { 1 => "on", 2 => "off", 3 => "starting", _ => "unknown" };

            return new HotspotState(
                Supported: true,
                State: state,
                ClientCount: clients,
                Ssid: ssid,
                Passphrase: passphrase,
                WifiQrPayload: BuildWifiPayload(ssid, passphrase),
                Error: null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure("Timed out talking to Windows Mobile Hotspot.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
        finally
        {
            _gate.Release();
        }

        static HotspotState Failure(string message) =>
            new(true, "unknown", 0, null, null, null, message);
    }

    private (string Ssid, string Passphrase) GetOrCreateCredentials()
    {
        if (_credentials is { } cached) return cached;

        try
        {
            if (File.Exists(_credentialsFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_credentialsFile));
                var ssid = doc.RootElement.GetProperty("ssid").GetString();
                var pass = doc.RootElement.GetProperty("passphrase").GetString();
                if (!string.IsNullOrWhiteSpace(ssid) && pass is { Length: >= 8 })
                    return (_credentials = (ssid, pass)).Value;
            }
        }
        catch { /* unreadable file - generate new credentials below */ }

        // Unambiguous characters only (no 0/O, 1/l/I) so the password is easy to read out if the QR can't be scanned.
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        var created = ($"LabelApp-{RandomNumberGenerator.GetInt32(1000, 10000)}", new string(chars));
        _credentials = created;
        try
        {
            File.WriteAllText(_credentialsFile, JsonSerializer.Serialize(new { ssid = created.Item1, passphrase = created.Item2 }));
        }
        catch { /* best-effort - the credentials still work for this run */ }
        return created;
    }

    // https://github.com/zxing/zxing/wiki/Barcode-Contents#wi-fi-network-config-android-ios-11
    private static string BuildWifiPayload(string ssid, string passphrase)
    {
        static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            foreach (var c in s)
            {
                // Backslash, semicolon, comma, colon and double quote must each be prefixed with a backslash.
                if (c is '\\' or ';' or ',' or ':' or '"')
                    sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }
        return $"WIFI:T:WPA;S:{Escape(ssid)};P:{Escape(passphrase)};;";
    }

    // Windows PowerShell 5.1. Reads HOTSPOT_ACTION (status|start|stop), HOTSPOT_SSID, HOTSPOT_PASS.
    // Prints exactly one JSON line: {ok, state, clients} or {ok:false, error}.
    private const string Script = """
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $ext = [System.WindowsRuntimeSystemExtensions]
    $asTaskGeneric = ($ext.GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
    $asTaskAction = ($ext.GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
    function Await($op, $type) {
        $t = $asTaskGeneric.MakeGenericMethod($type).Invoke($null, @($op))
        $t.Wait(-1) | Out-Null
        $t.Result
    }
    function AwaitAction($op) {
        $t = $asTaskAction.Invoke($null, @($op))
        $t.Wait(-1) | Out-Null
    }

    $netInfo = [Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType = WindowsRuntime]
    $mgrType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]
    $resType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]

    $profile = $netInfo::GetInternetConnectionProfile()
    if ($null -eq $profile) { $profile = $netInfo::GetConnectionProfiles() | Select-Object -First 1 }
    if ($null -eq $profile) { throw 'No network connection found. Windows needs this PC to be connected to a network (Ethernet or Wi-Fi) before it can create a hotspot.' }
    $tm = $mgrType::CreateFromConnectionProfile($profile)

    $action = $env:HOTSPOT_ACTION
    if ($action -eq 'start' -and [int]$tm.TetheringOperationalState -ne 1) {
        $cfg = $tm.GetCurrentAccessPointConfiguration()
        $cfg.Ssid = $env:HOTSPOT_SSID
        $cfg.Passphrase = $env:HOTSPOT_PASS
        AwaitAction ($tm.ConfigureAccessPointAsync($cfg))
        $r = Await ($tm.StartTetheringAsync()) $resType
        if ([int]$r.Status -ne 0) { throw ('Could not start the hotspot: ' + $r.Status + ' ' + $r.AdditionalErrorMessage) }
    }
    elseif ($action -eq 'stop' -and [int]$tm.TetheringOperationalState -eq 1) {
        $r = Await ($tm.StopTetheringAsync()) $resType
        if ([int]$r.Status -ne 0) { throw ('Could not stop the hotspot: ' + $r.Status + ' ' + $r.AdditionalErrorMessage) }
    }

    [pscustomobject]@{ ok = $true; state = [int]$tm.TetheringOperationalState; clients = [int]$tm.ClientCount } | ConvertTo-Json -Compress
}
catch {
    [pscustomobject]@{ ok = $false; error = $_.Exception.Message } | ConvertTo-Json -Compress
}
""";
}
