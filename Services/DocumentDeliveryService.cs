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
    /// Generates the PDF and sends it straight to the given printer.
    /// </summary>
    Task<IResult> PrintAsync(IDocument document, string printerName, string label, CancellationToken ct);
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

    public async Task<IResult> PrintAsync(IDocument document, string printerName, string label, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return Results.BadRequest(new { error = "printerName is required." });

        var filePath = Path.Combine(Path.GetTempPath(), $"doc-{Guid.NewGuid():N}.pdf");
        document.GeneratePdf(filePath);

        try
        {
            await _printer.PrintFileAsync(filePath, printerName, ct);
        }
        finally
        {
            File.Delete(filePath);
        }

        return Results.Ok(new { message = $"Sent {label} to printer '{printerName}'." });
    }
}
