# Tools/Fonts

Drop a Japanese-capable font file here (e.g. `NotoSansJP-Regular.ttf` from
https://fonts.google.com/noto/specimen/Noto+Sans+JP) so `PickingSlipPdfService` can render
検品書 (picking/inspection slip) text correctly. QuestPDF's builtin "Helvetica" - used by
`ShippingLabelPdfService` for the ASCII-only shipping label - has no CJK glyphs.

The `.csproj` copies everything in this folder next to the app's own binaries on every
build, and `Program.cs` registers the file at `Fonts:NotoSansJpPath` (see
`appsettings.json`, defaults to `Tools\Fonts\NotoSansJP-Regular.ttf`) with QuestPDF's
`FontManager.RegisterFont` at startup - the same "bundle it here, point a relative
appsettings.json path at it" pattern `Tools/SumatraPDF` uses for `SumatraPDF.exe`.

If this folder is empty, the app still starts and shipping labels still work; picking
slips render with missing glyphs until a font is placed here (a startup log warning says
so explicitly).
