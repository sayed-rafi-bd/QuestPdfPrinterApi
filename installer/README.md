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
The app listens on localhost only (port 5080, or the next free one). Logs: %LocalAppData%\LabelApp\app.log

To release a new version, change `Version` in the .csproj and `AppVersion` in installer\LabelApp.iss
(keep `AppId`), rebuild, and users can run the new Setup over the old one.
