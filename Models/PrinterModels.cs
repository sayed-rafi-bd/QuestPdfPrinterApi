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
