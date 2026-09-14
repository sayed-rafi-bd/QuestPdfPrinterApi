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
    public IDocument BuildProductsDocument(List<MockProduct> products, PageSize pageSize)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageSize);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text("Mock Products Report").FontSize(20).Bold();
                    col.Item().PaddingTop(2)
                        .Text($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}  ·  {products.Count} item(s)")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingVertical(15).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(35);
                        c.RelativeColumn(2);
                        c.RelativeColumn();
                        c.ConstantColumn(70);
                        c.ConstantColumn(50);
                        c.ConstantColumn(80);
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
                            .PaddingVertical(5)
                            .BorderBottom(1)
                            .BorderColor(Colors.Grey.Darken1);
                    });

                    foreach (var product in products)
                    {
                        table.Cell().Element(BodyCell).Text(product.Id.ToString());
                        table.Cell().Element(BodyCell).Text(product.Name);
                        table.Cell().Element(BodyCell).Text(product.Category);
                        table.Cell().Element(BodyCell).Text($"${product.Price:0.00}");
                        table.Cell().Element(BodyCell).Text(product.Stock.ToString());
                        table.Cell().Element(BodyCell).Text(product.CreatedAt.ToString("yyyy-MM-dd"));
                    }

                    static IContainer BodyCell(IContainer c) => c
                        .PaddingVertical(4)
                        .BorderBottom(1)
                        .BorderColor(Colors.Grey.Lighten2);
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
