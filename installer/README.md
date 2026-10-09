# Building the installer (Option B)

Needs a Windows PC with the .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isdl.php).

1. Make sure these exist (they are bundled into the installer):
   - `Tools\SumatraPDF\SumatraPDF.exe`
   - `Tools\Fonts\NotoSansJP-Regular.ttf`  <- not in the repo yet, add it
2. From the project folder in PowerShell: `.\build-installer.ps1`
3. Share `installer\Output\ShippingLabelPrinter-Setup-1.0.0.exe`.

What users get: Start Menu shortcut (opens the UI, starting the app if needed), optional desktop
shortcut, optional start-at-login, a "Stop" shortcut, and an uninstaller. It installs per user
(no admin prompt). It runs as the signed-in user, not a service, so it sees that user's printers.
Phone hotspot: with network access on, the UI can start Windows' Mobile Hotspot, show a Wi-Fi QR code, and once a phone has joined show a QR code with the app address. Needs Windows 10+, a hotspot-capable Wi-Fi adapter, and some network connection on the PC to share. Starting it sets the hotspot name/password to this app's own (kept in %LocalAppData%\LabelApp\hotspot.json).

The app listens on localhost only (port 5080, or the next free one) unless the "Allow phones/tablets on my network" option is ticked in the installer; then the UI shows the address to open on a phone at the top of the page. First start may trigger a Windows Firewall prompt - allow it on private networks. Logs: %LocalAppData%\LabelApp\app.log

To release a new version, change `Version` in the .csproj and `AppVersion` in installer\LabelApp.iss
(keep `AppId`), rebuild, and users can run the new Setup over the old one.
