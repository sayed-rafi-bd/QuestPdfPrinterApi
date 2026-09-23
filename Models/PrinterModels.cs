namespace QuestPdfPrinterApi.Models;

/// <summary>
/// One entry from GET /api/printers. Any printer Windows itself can see - USB, network/IP,
/// AirPrint/IPP, or Bluetooth - shows up here once it's installed as a Windows print queue
/// (Settings -> Bluetooth &amp; devices -> Printers &amp; scanners -> Add device for Bluetooth
/// ones); nothing about PrinterService is restricted to a particular connection type.
/// PortName/IsBluetooth just help a caller confirm *which* queue they're looking at when a
/// machine has several - Bluetooth printers commonly show a PortName under "BTH" (e.g.
/// "BTHENUM\..." or a plain "BTH:..." port), which is how IsBluetooth is detected. This is a
/// best-effort heuristic, not a guarantee: some Bluetooth printers register under a COM port
/// or a manufacturer-named virtual port instead, so a printer can be Bluetooth-connected even
/// if IsBluetooth comes back false. When in doubt, match on Name.
/// </summary>
public record PrinterInfo(string Name, string PortName, bool IsBluetooth);

/// <summary>
/// How a print job should be scaled onto the printer's currently-configured paper/label
/// size. Passed per-request (see ShippingLabelsPrintRequest.FitMode) rather than fixed,
/// because the right choice depends on whether the target printer's paper size is known
/// to match the PDF's own page size (see PrinterService.PrintFileAsync's remarks).
/// </summary>
public enum PrintFitMode
{
    /// <summary>
    /// Print at the PDF's exact page size with no rescaling (SumatraPDF's "noscale").
    /// The default - correct whenever the PDF was generated at the same physical size as
    /// the label/paper actually loaded (e.g. this API's 110mm x 84mm default), since any
    /// rescale step there is pure downside: it softens text and barcodes for no benefit.
    /// If the printer's configured paper size doesn't actually match, mismatches show up
    /// as an offset/clipped print rather than being silently papered over - which is the
    /// right failure mode, since it points at the real misconfiguration instead of hiding it.
    /// </summary>
    Fit,

    /// <summary>
    /// Scale the whole page to fit within whatever paper size the printer driver is
    /// currently configured for, preserving aspect ratio - letterboxed (not cropped) if the
    /// aspect ratios differ (SumatraPDF's "fit"). Use this when the PDF's page size and the
    /// printer's configured paper size are NOT known to match - e.g. previewing a label on
    /// a standard A4 office printer - at the cost of a rescale step that can soften text and
    /// barcodes on a real label printer.
    /// </summary>
    Contain
}

/// <summary>
/// Which way up the page is generated/printed. Passed per-request rather than baked into a
/// single named page size, because the same physical dimensions (e.g. this API's 110mm x
/// 84mm default label) can be wanted either way depending on how the label stock is loaded
/// in a given printer.
/// </summary>
public enum PageOrientation
{
    /// <summary>Width is the larger dimension - the default for this API's 110mm x 84mm label.</summary>
    Landscape,

    /// <summary>Height is the larger dimension. Swaps the resolved page size's width/height if needed.</summary>
    Portrait
}
