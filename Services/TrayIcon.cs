#if WINDOWS
using System.Drawing;
using System.Windows.Forms;
#endif

namespace QuestPdfPrinterApi.Services;

/// <summary>
/// Small icon in the Windows system tray (bottom-right of the taskbar) so the app is visible
/// while it runs in the background: click it to open the UI, right-click for Open / Exit.
///
/// A WinForms NotifyIcon needs its own STA thread with a message loop, so everything here runs
/// on a dedicated background thread and never touches the web server's threads. On non-Windows
/// builds (plain net8.0, see the .csproj) this compiles to a no-op.
/// </summary>
public sealed class TrayIcon : IDisposable
{
#if WINDOWS
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Control? _marshal;          // lets Dispose() hop onto the tray thread
    private long _lastOpenTicks;
    private bool _disposed;

    public TrayIcon(string tooltip, Action onOpen, Action onExit)
    {
        _thread = new Thread(() => Run(tooltip, onOpen, onExit)) { IsBackground = true, Name = "TrayIcon" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run(string tooltip, Action onOpen, Action onExit)
    {
        NotifyIcon? icon = null;
        ContextMenuStrip? menu = null;
        try
        {
            try { Application.SetHighDpiMode(HighDpiMode.SystemAware); } catch { /* cosmetic only */ }
            Application.EnableVisualStyles();

            _marshal = new Control();
            _ = _marshal.Handle;        // force the window handle to be created on this thread

            // Single left click = open. Debounced so a double-click doesn't open two tabs.
            void Open()
            {
                var now = Environment.TickCount64;
                if (now - _lastOpenTicks < 800) return;
                _lastOpenTicks = now;
                onOpen();
            }

            menu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Open Shipping Label Printer") { Font = new Font(menu.Font, FontStyle.Bold) };
            openItem.Click += (_, _) => Open();
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => onExit();
            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = tooltip,         // max 127 chars
                ContextMenuStrip = menu,
                Visible = true
            };
            icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Open(); };

            _ready.Set();
            Application.Run();          // message loop - returns when Dispose() calls ExitThread
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TRAY] Could not create the tray icon: {ex.Message}");
        }
        finally
        {
            _ready.Set();
            // Hide first, otherwise Windows leaves a ghost icon until the mouse moves over it.
            if (icon != null) { icon.Visible = false; icon.Dispose(); }
            menu?.Dispose();
            _marshal?.Dispose();
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            // Assets\icon.ico is copied next to the exe (see .csproj). Passing the small-icon size
            // makes Windows pick the right image from the multi-size .ico, so it stays sharp.
            var file = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(file)) return new Icon(file, SystemInformation.SmallIconSize);

            // Fallback: the icon embedded in the exe (ApplicationIcon).
            var exe = Environment.ProcessPath;
            if (exe != null && Icon.ExtractAssociatedIcon(exe) is { } embedded) return embedded;
        }
        catch { /* fall through */ }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _ready.Wait(TimeSpan.FromSeconds(2));
            if (_marshal is { IsHandleCreated: true })
                _marshal.BeginInvoke(new Action(() => Application.ExitThread()));
            _thread.Join(TimeSpan.FromSeconds(2));
        }
        catch { /* shutting down anyway */ }
    }
#else
    public TrayIcon(string tooltip, Action onOpen, Action onExit) { }
    public void Dispose() { }
#endif
}
