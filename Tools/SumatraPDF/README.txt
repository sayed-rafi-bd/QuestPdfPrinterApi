Put the portable SumatraPDF.exe build here (this file just keeps the empty folder in git).

1. Download the "Portable" 64-bit build from https://www.sumatrapdfreader.org/download-free-pdf-viewer
2. Extract it, and copy SumatraPDF.exe into this folder (QuestPdfPrinterApi/Tools/SumatraPDF/SumatraPDF.exe).
3. Build the project - the .csproj copies this whole folder next to the app's binaries
   automatically, so no absolute path or machine-specific setup is needed.

appsettings.json already points Printing:SumatraPdfPath at this relative location. Only
override it if you'd rather keep SumatraPDF somewhere outside the project.
