using QuestPDF.Helpers;
using QuestPdfPrinterApi.Models;

namespace QuestPdfPrinterApi.Services;

public interface IMockProductsSource
{
    List<MockProduct> GenerateProducts(int count);
}

/// <summary>
/// Backs the mock GET /api/mock/products endpoint. Generates `count` random-but-plausible
/// products using QuestPDF.Helpers.Placeholders (the same generator style used elsewhere)
/// so it needs no database or external service - swap this for a real repository later
/// without touching the endpoint or the PDF code.
/// </summary>
public class MockProductsSource : IMockProductsSource
{
    private static readonly Random Random = new();

    public List<MockProduct> GenerateProducts(int count)
    {
        return Enumerable.Range(1, count)
            .Select(id => new MockProduct
            {
                Id = id,
                Name = Placeholders.Label(),
                Category = Placeholders.Label(),
                Price = (decimal)Math.Round(Random.NextDouble() * 500, 2),
                Stock = Random.Next(0, 200),
                CreatedAt = DateTime.Now.AddDays(-Random.Next(0, 365))
            })
            .ToList();
    }
}
