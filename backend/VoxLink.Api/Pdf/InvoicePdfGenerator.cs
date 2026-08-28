using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VoxLink.Api.Pdf;

public record InvoiceLineItem(string Description, decimal Amount);

public static class InvoicePdfGenerator
{
    public static byte[] Generate(
        string companyName,
        Guid invoiceId,
        DateTimeOffset issuedAt,
        DateOnly? dueDate,
        IReadOnlyList<InvoiceLineItem> lineItems,
        decimal amountDue,
        BillingOptions banking)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text("VoxLink").FontSize(20).Bold();
                    col.Item().Text($"Invoice #{invoiceId.ToString()[..8].ToUpper()}").FontSize(12).FontColor(Colors.Grey.Darken1);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().PaddingTop(10).Text($"Billed to: {companyName}");
                    col.Item().Text($"Issued: {issuedAt:yyyy-MM-dd}");
                    if (dueDate is not null) col.Item().Text($"Due: {dueDate:yyyy-MM-dd}");

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Description").Bold();
                            header.Cell().AlignRight().Text("Amount (ZAR)").Bold();
                        });

                        foreach (var item in lineItems)
                        {
                            table.Cell().Text(item.Description);
                            table.Cell().AlignRight().Text($"R {item.Amount:0.00}");
                        }
                    });

                    col.Item().PaddingTop(10).AlignRight().Text($"Total due: R {amountDue:0.00}").FontSize(14).Bold();

                    col.Item().PaddingTop(20).Text("Payment details").Bold();
                    col.Item().Text($"Bank: {banking.BankName}");
                    col.Item().Text($"Account holder: {banking.PayeeName}");
                    col.Item().Text($"Account number: {banking.AccountNumber}");
                    col.Item().Text($"Account type: {banking.AccountType}");
                    col.Item().PaddingTop(6).Text(
                        "Please use this invoice number as your payment reference, and upload proof of " +
                        "payment via the VoxLink platform once paid.").FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("VoxLink — ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    t.Span(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }
}
