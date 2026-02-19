using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using AppleShopikCRM.Client.Models;

namespace AppleShopikCRM.Server.Services
{
    public class PdfService
    {
        public byte[] GenerateReceiptPdf(SaleReceiptDto data)
        {
            // Ліцензія Community (безкоштовна)
            QuestPDF.Settings.License = LicenseType.Community;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // 🔥 ВИПРАВЛЕННЯ ТУТ:
                    // Замість фіксованого PageSize використовуємо ContinuousSize (Нескінченна стрічка)
                    // 80 мм - стандартна ширина чеку, висота - автоматична
                    page.ContinuousSize(52, Unit.Millimetre);

                    page.Margin(5, Unit.Millimetre);

                    // Використовуємо стандартний шрифт, щоб уникнути помилок з Fonts.Courier
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Content().Column(col =>
                    {
                        // 1. Шапка
                        col.Item().Text(data.StoreName).Bold().FontSize(12).AlignCenter();
                        col.Item().Text(data.StoreAddress).FontSize(8).AlignCenter();
                        col.Item().Text($"Тел: {data.StorePhone}").FontSize(8).AlignCenter();
                        col.Item().Text("--------------------------------").AlignCenter();

                        // 2. Інфо
                        col.Item().Text($"Чек #: {data.Sale.Id}").FontSize(9);
                        col.Item().Text($"Дата: {data.Sale.SaleDate:dd.MM.yyyy HH:mm}").FontSize(9);
                        col.Item().Text($"Касир: {data.SellerName}").FontSize(9);
                        col.Item().Text("--------------------------------").AlignCenter();

                        // 3. Товари
                        foreach (var item in data.Sale.SaleItems)
                        {
                            // Назва товару
                            col.Item().Text(item.ProductName).FontSize(9).Bold();

                            // Серійний номер (якщо є)
                            if (!string.IsNullOrEmpty(item.Identifier) && item.Identifier.Length > 2)
                            {
                                col.Item().Text($"SN: {item.Identifier}").FontSize(7).FontColor(Colors.Grey.Darken2);
                            }

                            // Ціна та сума
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"{item.Quantity} x {item.SoldSellingPrice:N0}").FontSize(9);
                                row.AutoItem().Text($"= {(item.Quantity * item.SoldSellingPrice):N0}").FontSize(9);
                            });

                            // Відступ між товарами
                            col.Item().PaddingBottom(2);
                        }

                        col.Item().Text("--------------------------------").AlignCenter();

                        // 4. Підсумки
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("ДО СПЛАТИ:").Bold().FontSize(11);
                            row.AutoItem().Text($"{data.Sale.FinalAmount:N0} грн").Bold().FontSize(11);
                        });

                        // Знижка (показуємо тільки якщо вона є)
                        if (data.Sale.TotalAmount > data.Sale.FinalAmount)
                        {
                            col.Item().Text($"Знижка: {(data.Sale.TotalAmount - data.Sale.FinalAmount):N0} грн").FontSize(8).Italic();
                        }

                        col.Item().PaddingTop(5);

                        // 5. Оплати (Готівка/Картка)
                        foreach (var p in data.Sale.Payments)
                        {
                            string payInfo = $"Оплата ({p.Method}): {p.Amount:N0}";
                            if (p.Method == "USD" || p.Method == "EUR")
                            {
                                payInfo += $" (Курс {p.ExchangeRate})";
                            }
                            col.Item().Text(payInfo).FontSize(9);
                        }

                        // 6. Футер
                        col.Item().PaddingTop(10);
                        col.Item().Text("Дякуємо за покупку!").AlignCenter().Bold().FontSize(10);
                        col.Item().Text("Гарантія 30 днів").AlignCenter().FontSize(8);

                        // Відступ знизу для зручного відриву
                        col.Item().PaddingBottom(10);
                    });
                });
            });

            return document.GeneratePdf();
        }
    }
}