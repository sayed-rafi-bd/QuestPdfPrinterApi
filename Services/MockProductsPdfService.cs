using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPdfPrinterApi.Models;

namespace QuestPdfPrinterApi.Services;

public interface IMockProductsPdfService
{
    IDocument BuildProductsDocument(List<MockProduct> products, PageSize pageSize);
}

public class MockProductsPdfService : IMockProductsPdfService
{
    private const int RowsPerPage = 10;

    public IDocument BuildProductsDocument(List<MockProduct> products, PageSize pageSize)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageSize);
                page.MarginVertical(20);
                page.MarginHorizontal(25);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("Mock Products Report").FontSize(16).Bold();
                    col.Item().PaddingTop(1)
                        .Text($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}  ·  {products.Count} item(s)")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(content =>
                {
                    var chunks = products.Chunk(RowsPerPage).ToList();

                    for (int i = 0; i < chunks.Count; i++)
                    {
                        if (i > 0)
                            content.Item().PageBreak();

                        content.Item().PaddingVertical(6).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(25);
                                c.RelativeColumn(2);
                                c.RelativeColumn();
                                c.ConstantColumn(55);
                                c.ConstantColumn(40);
                                c.ConstantColumn(65);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("#");
                                header.Cell().Element(HeaderCell).Text("Name");
                                header.Cell().Element(HeaderCell).Text("Category");
                                header.Cell().Element(HeaderCell).Text("Price");
                                header.Cell().Element(HeaderCell).Text("Stock");
                                header.Cell().Element(HeaderCell).Text("Created");

                                static IContainer HeaderCell(IContainer c) => c
                                    .DefaultTextStyle(x => x.Bold())
                                    .PaddingVertical(2)
                                    .BorderBottom(0.5f)
                                    .BorderColor(Colors.Grey.Darken1);
                            });

                            foreach (var product in chunks[i])
                            {
                                table.Cell().Element(BodyCell).Text(product.Id.ToString());
                                table.Cell().Element(BodyCell).Text(product.Name);
                                table.Cell().Element(BodyCell).Text(product.Category);
                                table.Cell().Element(BodyCell).Text($"${product.Price:0.00}");
                                table.Cell().Element(BodyCell).Text(product.Stock.ToString());
                                table.Cell().Element(BodyCell).Text(product.CreatedAt.ToString("yyyy-MM-dd"));
                            }

                            static IContainer BodyCell(IContainer c) => c
                                .PaddingVertical(2)
                                .BorderBottom(0.5f)
                                .BorderColor(Colors.Grey.Lighten2);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        });

        return document;
    }
}
