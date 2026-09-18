using QuestPDF.Companion;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace QuestPdfPrinterApi.Services;

public interface IDocumentDeliveryService
{
    /// <summary>
    /// Pushes the document to a running Companion App instance for live preview.
    /// Nothing is saved or printed. "label" only shows up in the response message
    /// (e.g. "mock products report").
    /// </summary>
    IResult Preview(IDocument document, int? companionPort, string label);

    /// <summary>
    /// Generates the PDF and sends it straight to the given printer. dpiOverride is
    /// optional and per-job - see PrinterService's remarks on why it's opt-in rather than
    /// a single global setting (in short: not every printer, especially Bluetooth/thermal
    /// label printers, supports the same resolution).
    /// </summary>
    Task<IResult> PrintAsync(IDocument document, string printerName, string label, int? dpiOverride, CancellationToken ct);
}

public class DocumentDeliveryService : IDocumentDeliveryService
{
    private readonly IPrinterService _printer;

    public DocumentDeliveryService(IPrinterService printer)
    {
        _printer = printer;
    }

    public IResult Preview(IDocument document, int? companionPort, string label)
    {
        // Requires the Companion App to already be running and listening on this
        // port, on the same machine this process runs on (see earlier caveat).
        var port = companionPort ?? 12500;
        document.ShowInCompanion(port);

        return Results.Ok(new
        {
            message = $"Sent {label} to the Companion App on port {port}.",
            note = "Open the Companion App to see it - nothing is saved or printed in this mode."
        });
    }

    public async Task<IResult> PrintAsync(IDocument document, string printerName, string label, int? dpiOverride, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return Results.BadRequest(new { error = "printerName is required." });

        var filePath = Path.Combine(Path.GetTempPath(), $"doc-{Guid.NewGuid():N}.pdf");
        document.GeneratePdf(filePath);

        // Fire-and-forget: hand the job to the OS print pipeline (SumatraPDF/lp) without
        // waiting for it to finish. That wait is real OS/driver time we can't shrink, so
        // we no longer block the HTTP response on it. Use CancellationToken.None here -
        // the request's ct gets cancelled as soon as the response is sent, which would
        // otherwise abort the print job right after we told the caller it was queued.
        _ = Task.Run(async () =>
        {
            try
            {
                await _printer.PrintFileAsync(filePath, printerName, dpiOverride, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PRINT ERROR] '{label}' to printer '{printerName}' failed: {ex}");
            }
            finally
            {
                try { File.Delete(filePath); }
                catch (Exception ex) { Console.Error.WriteLine($"[PRINT ERROR] Failed to delete temp file '{filePath}': {ex}"); }
            }
        });

        return Results.Ok(new { message = $"Queued {label} for printer '{printerName}'. Check server logs for completion/errors." });
    }
}
