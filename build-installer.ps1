# Builds Setup.exe:  publish (self-contained, no console window)  ->  Inno Setup compiler.
# Run from the project folder in PowerShell:  .\build-installer.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

foreach ($f in @("Tools\SumatraPDF\SumatraPDF.exe", "Tools\Fonts\NotoSansJP-Regular.ttf")) {
    if (-not (Test-Path $f)) { throw "Missing $f - add it before building the installer." }
}

if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish QuestPdfPrinterApi.csproj -c Release -r win-x64 --self-contained true -p:HideConsole=true -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php and run this again." }

& $iscc "installer\LabelApp.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

Write-Host "Done: installer\Output\ShippingLabelPrinter-Setup-1.0.0.exe"
