using AppleShopikCRM.Client.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using AppleShopikCRM.Client.Models;

namespace AppleShopikCRM.Server.Services
{
    using AppleShopikCRM.Helpers;
    using QuestPDF.Fluent;
    using QuestPDF.Helpers;
    using QuestPDF.Infrastructure;
    using System.Text.RegularExpressions;
    using static System.Runtime.InteropServices.JavaScript.JSType;

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

                                    row.RelativeItem().Text(warrantyName).FontSize(9).Italic();
                                    row.AutoItem().Text($"{warrantyPrice:N0}").FontSize(9).Italic();
                                });
                            }

                            // Відступ між різними товарами
                            col.Item().PaddingBottom(3);
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

                                // Вставляємо картинку по центру
                                col.Item().PaddingTop(10).AlignCenter().Width(60).Image(qrBytes);
                                col.Item().Text("Ми в Instagram").AlignCenter().FontSize(8).Bold();
                            }
                            catch
                            {
                                // Якщо немає інтернету, чек згенерується просто без QR-коду, щоб каса не зависла
                            }
                        }
                        // Відступ знизу для зручного відриву
                        col.Item().PaddingTop(6);
                        col.Item().Text("ФІСКАЛЬНИЙ ЧЕК").AlignCenter().FontSize(4);
                        col.Item().PaddingBottom(10);
                    });
                });
            });

            return document.GeneratePdf();
        }

        public byte[] GenerateRepairBlankPdf(RepairTicket ticket, string managerName, string storeName, string storeAddress, string storePhone, string storeInstagram, string fullDeviceName)
        {
            // Ліцензія Community (безкоштовна)
            QuestPDF.Settings.License = LicenseType.Community;

            // 1. Розбираємо Опис, Код та Стан
            string issue = ticket.IssueDescription ?? "";
            string passcode = "-";
            string condition = "ok";

            var codeMatch = Regex.Match(issue, @"\[Код:\s*(.*?)\]");
            if (codeMatch.Success) { passcode = codeMatch.Groups[1].Value; issue = issue.Replace(codeMatch.Value, ""); }

            var conditionMatch = Regex.Match(issue, @"\[Стан:\s*(.*?)\]");
            if (conditionMatch.Success) { condition = conditionMatch.Groups[1].Value; issue = issue.Replace(conditionMatch.Value, ""); }

            issue = issue.Trim();
            string clientInfo = ticket.Type == RepairType.ClientService ? $"{ticket.ClientName} {ticket.ClientPhone}" : "Складський пристрій";

            // 2. Завантажуємо QR-код
            byte[]? qrBytes = null;
            if (!string.IsNullOrEmpty(storeInstagram))
            {
                try
                {
                    using var client = new System.Net.Http.HttpClient();
                    string qrUrl = $"https://bwipjs-api.metafloor.com/?bcid=qrcode&text={storeInstagram}&scale=3";
                    qrBytes = client.GetByteArrayAsync(qrUrl).Result;
                }
                catch { }
            }

            // 3. Генеруємо документ (ФОРМАТ ЧЕКА 58мм)
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // Нескінченна стрічка: 52 мм (ширина друку для 58 мм паперу)
                    page.ContinuousSize(58, Unit.Millimetre);

                    // Задаємо відступи вручну, щоб вирівняти текст
                    page.MarginLeft(1, Unit.Millimetre);  // Мінімальний відступ зліва (тягнемо текст вліво)
                    page.MarginRight(6, Unit.Millimetre); // Більший відступ справа
                    page.MarginTop(5, Unit.Millimetre);
                    page.MarginBottom(5, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Content().Column(col =>
                    {
                        // ВЕРХНЯ КОПІЯ (Для клієнта)
                        DrawRepairBlankNarrow(col, ticket, fullDeviceName, clientInfo, issue, passcode, condition, managerName, storeName, storeAddress, storePhone, qrBytes, "ЕКЗЕМПЛЯР КЛІЄНТА");

                        // ЛІНІЯ ВІДРІЗУ
                        col.Item().PaddingVertical(15).AlignCenter().Text("- - - - ✂ ЛІНІЯ ВІДРІЗУ ✂ - - - -").FontSize(8).FontColor(Colors.Grey.Medium);

                        // НИЖНЯ КОПІЯ (Для магазину)
                        DrawRepairBlankNarrow(col, ticket, fullDeviceName, clientInfo, issue, passcode, condition, managerName, storeName, storeAddress, storePhone, qrBytes, "ЕКЗЕМПЛЯР МАГАЗИНУ");
                    });
                });
            });

            return document.GeneratePdf();
        }

        // Допоміжний метод для малювання вузького бланка на чековій стрічці
        private void DrawRepairBlankNarrow(ColumnDescriptor col, RepairTicket t, string deviceName, string clientInfo, string issue, string passcode, string condition, string managerName, string storeName, string storeAddress, string storePhone, byte[]? qrBytes, string copyType)
        {
            // Шапка
            col.Item().AlignCenter().Text($"AppleShopik {storeName}").FontSize(13).Bold();
            col.Item().PaddingBottom(15).AlignCenter().Text("Техніка та Аксесуари").FontSize(9);

            // Інформація про чек
            col.Item().Text($"Бланк ремонту № {t.TicketNumber}").Bold().FontSize(10);
            col.Item().Text($"Дата: {TimeHelper.GetUkraineTime():dd.MM.yyyy HH:mm}");
            col.Item().PaddingBottom(5).Text($"Продавець: {managerName}");

            col.Item().PaddingBottom(5).Text("------------------------------------------------").AlignCenter();

            // Деталі прийому (На вузькому папері краще використовувати "Ключ: Значення" замість таблиці з рамками)
            void AddRow(string label, string value)
            {
                col.Item().PaddingBottom(3).Text(txt =>
                {
                    txt.Span(label + ": ").SemiBold();
                    txt.Span(value);
                });
            }

            AddRow("Клієнт", clientInfo);
            AddRow("Модель", deviceName);
            AddRow("IMEI", t.SerialNumber!);
            AddRow("Несправність", issue);
            AddRow("Код / iCloud", passcode);
            AddRow("Стан", condition);

            col.Item().PaddingTop(5).Text("------------------------------------------------").AlignCenter();

            // Місце для підпису
            col.Item().PaddingTop(10).PaddingBottom(15).Text("Підпис клієнта:\n\n___________________________");

            // Контакти та QR
            col.Item().AlignCenter().Text(storeAddress).FontSize(8);
            col.Item().PaddingBottom(5).AlignCenter().Text($"тел. {storePhone}").FontSize(8);

            if (qrBytes != null)
            {
                col.Item().AlignCenter().Width(65).Height(65).Image(qrBytes);
                col.Item().PaddingTop(2).AlignCenter().Text("Ми в Instagram").FontSize(8).Bold();
            }
        }
        // 🔥 Генерація красивого цифрового чеку/гарантії для Telegram (Формат A5)
        public byte[] GenerateDigitalWarrantyPdf(SaleReceiptDto data)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var document = Document.Create(container =>
            {
                // ==========================================
                // СТОРІНКА 1: ТОВАРНИЙ ЧЕК
                // ==========================================
                container.Page(page =>
                {
                    page.Size(PageSizes.A5);
                    page.Margin(15, Unit.Millimetre);
                    page.MarginTop(5, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    // Шапка чека
                    page.Header().Column(col =>
                    {
                        if (System.IO.File.Exists("wwwroot/images/logo.png"))
                        {
                            col.Item().AlignCenter().Height(100).Image("wwwroot/images/logo.png");
                        }

                        // 🔥 TranslateY(-30) візуально підніме текст на 30 точок вгору, ПРЯМО на біле поле логотипу.
                        // PaddingBottom(20) збереже відступ від таблиці, яка йде нижче.
                        col.Item()
                           .TranslateY(-20)
                           .AlignCenter()
                           .PaddingBottom(20)
                           .Text("ЕЛЕКТРОННИЙ ЧЕК").FontSize(12).Bold();
                    });

                    // Вміст чека
                    page.Content().Column(col =>
                    {
                        // Інформація про покупку (Чек, Дата, Клієнт, Продавець)
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });

                            // Ліва колонка
                            table.Cell().Column(c =>
                            {
                                c.Item().Text($"Чек №: {data.Sale.Id}").Bold();
                                c.Item().Text($"Дата: {data.Sale.SaleDate:dd.MM.yyyy HH:mm}");
                            });

                            // Права колонка
                            table.Cell().AlignRight().Column(c =>
                            {
                                string customerName = data.Sale.Customer?.Name ?? "Покупець";
                                c.Item().Text($"Клієнт: {customerName}");
                                c.Item().Text($"Продавець: {data.SellerName}");
                            });
                        });

                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#e5e5ea");

                        // Таблиця товарів
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4); // Назва
                                columns.RelativeColumn(1); // К-сть
                                columns.RelativeColumn(2); // Ціна
                                columns.RelativeColumn(2); // Сума
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Товар").Bold();
                                header.Cell().AlignCenter().Text("К-сть").Bold();
                                header.Cell().AlignRight().Text("Ціна").Bold();
                                header.Cell().AlignRight().Text("Сума").Bold();
                            });

                            foreach (var item in data.Sale.SaleItems)
                            {
                                decimal basePrice = item.SoldSellingPrice;
                                string displayName = item.ProductName ?? "Товар";
                                if (!string.IsNullOrEmpty(item.Identifier))
                                    displayName += $"\n(IMEI: {item.Identifier})";

                                table.Cell().PaddingVertical(4).Text(displayName);
                                table.Cell().PaddingVertical(4).AlignCenter().Text(item.Quantity.ToString());
                                table.Cell().PaddingVertical(4).AlignRight().Text($"{basePrice:N0}");
                                table.Cell().PaddingVertical(4).AlignRight().Text($"{(item.Quantity * basePrice):N0}");

                                // Гарантія (якщо є) - виводимо в чеку як додаткову послугу
                                if (item.WarrantyPrice > 0)
                                {
                                    int term = item.WarrantyRate?.TermMonths ?? 0;
                                    table.Cell().Text($"+ Гарантія ({term} міс.)").FontSize(9).Italic();
                                    table.Cell().AlignCenter().Text("1").FontSize(9).Italic();
                                    table.Cell().AlignRight().Text($"{item.WarrantyPrice:N0}").FontSize(9).Italic();
                                    table.Cell().AlignRight().Text($"{item.WarrantyPrice:N0}").FontSize(9).Italic();
                                }
                            }
                        });

                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#e5e5ea");

                        // Оплата та Підсумки (Надійна структура через Table)
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); });

                            // Ліва колонка: Методи оплати
                            table.Cell().Column(c =>
                            {
                                c.Item().Text("Оплата:").Bold();
                                if (data.Sale.Payments != null && data.Sale.Payments.Any())
                                {
                                    foreach (var p in data.Sale.Payments)
                                    {
                                        c.Item().Text($"• {p.Method}: {p.Amount:N0} {(p.Method == "USD" || p.Method == "EUR" ? p.Method : "грн")}").FontSize(9);
                                    }
                                }
                                else
                                {
                                    c.Item().Text("Не вказано").FontSize(9);
                                }
                            });

                            // Права колонка: Загальна сума
                            table.Cell().AlignRight().Column(c =>
                            {
                                if (data.GlobalDiscount > 0)
                                {
                                    c.Item().AlignRight().Text($"Знижка: {data.GlobalDiscount:N0} грн").FontSize(10).FontColor("#8e8e93");
                                }
                                c.Item().AlignRight().Text($"Всього до сплати: {data.Sale.FinalAmount:N0} грн").FontSize(14).Bold();
                            });
                        });
                    });

                    // Футер чека (Справжнє клікабельне посилання)
                    page.Footer().AlignCenter().Column(col =>
                    {
                        col.Item().AlignCenter().Text($"Тел: {data.StorePhone}").FontSize(9).Bold();
                        col.Item().AlignCenter().PaddingBottom(2).Text(data.StoreAddress).FontSize(9).Bold();
                        col.Item().AlignCenter().Row(row =>
                        {
                            // Відступ між посиланнями
                            row.Spacing(15);

                            // 1. Instagram
                            if (!string.IsNullOrEmpty(data.StoreInstagramUrl))
                            {
                                row.AutoItem()
                                   .Hyperlink(data.StoreInstagramUrl) // Справжнє довге посилання (невидиме)
                                   .Text("Ми в Instagram")            // Короткий красивий текст
                                   .FontSize(9).FontColor("#007aff").Underline();
                            }

                            // 2. Telegram
                            // Якщо у вас в DTO є поле data.StoreTelegramUrl, використовуйте його.
                            // Якщо ні - можете тимчасово вписати пряме посилання замість змінної, 
                            // наприклад: .Hyperlink("https://t.me/appleshopik_kosiv")
                            string telegramUrl = "https://t.me/+xG9hRsE_AXFiMjc6"; // ЗАМІНІТЬ НА СВОЄ, ЯКЩО НЕМАЄ В БД

                            if (!string.IsNullOrEmpty(telegramUrl))
                            {
                                row.AutoItem()
                                   .Hyperlink(telegramUrl)
                                   .Text("Ми в Telegram")
                                   .FontSize(9).FontColor("#007aff").Underline();
                            }
                        });
                    });
                });

                // ==========================================
                // СТОРІНКА 2: ГАРАНТІЙНИЙ ТАЛОН 
                // (генерується ТІЛЬКИ якщо є товари з гарантією)
                // ==========================================
                var warranties = data.Sale.CustomerWarranties?.ToList() ?? new List<CustomerWarranty>();

                if (warranties.Any())
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A5);
                        page.Margin(15, Unit.Millimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                        page.Header().Column(col =>
                        {
                            if (System.IO.File.Exists("wwwroot/images/logo.png"))
                            {
                                col.Item().AlignCenter().Height(100).Image("wwwroot/images/logo.png");
                            }

                            // 🔥 TranslateY(-30) візуально підніме текст на 30 точок вгору, ПРЯМО на біле поле логотипу.
                            // PaddingBottom(20) збереже відступ від таблиці, яка йде нижче.
                            col.Item()
                               .TranslateY(-20)
                               .AlignCenter()
                               .PaddingBottom(20)
                               .Text("ГАРАНТІЙНИЙ ТАЛОН").FontSize(12).Bold();
                        });

                        page.Content().Column(col =>
                        {
                            col.Item().PaddingBottom(15).Text($"Цей документ підтверджує право покупця на гарантійне обслуговування перелічених нижче пристроїв.").FontSize(10);

                            // Деталі кожного пристрою з гарантією
                            foreach (var warranty in warranties)
                            {
                                col.Item().ShowEntire().Background("#f5f5f7").Padding(10).Column(c =>
                                {
                                    c.Item().Text(txt => { txt.Span("Пристрій: ").Bold(); txt.Span(warranty.DeviceModel); });
                                    c.Item().Text(txt => { txt.Span("IMEI / SN: ").Bold(); txt.Span(warranty.Imei ?? "Не вказано"); });
                                    c.Item().Text(txt => { txt.Span("Тип гарантії: ").Bold(); txt.Span($"{warranty.WarrantyType} ({warranty.TermMonths} міс.)"); });

                                    c.Item().PaddingTop(5).Text(txt =>
                                    {
                                        txt.Span("Дійсна з: ").FontSize(10);
                                        txt.Span($"{warranty.StartDate:dd.MM.yyyy} ").FontSize(10).Bold();
                                        txt.Span("по: ").FontSize(10);

                                        // 🔥 Тепер дата закінчення чітко береться з БД, а не вираховується "на льоту"
                                        txt.Span($"{warranty.EndDate:dd.MM.yyyy}").FontSize(10).Bold().FontColor("#ff3b30");
                                    });
                                });
                                col.Item().PaddingBottom(10);
                            }

                            // Умови гарантії
                            col.Item().PaddingTop(15).LineHorizontal(1).LineColor("#e5e5ea");
                            col.Item().PaddingTop(10).Text("Умови гарантійного обслуговування:").Bold().FontSize(10);
                            col.Item().Text("1. Гарантія дійсна лише за наявності цього талону (в електронному або паперовому вигляді).").FontSize(9);
                        });

                        page.Footer().AlignCenter().Text("З умовами гарантії ознайомлений та погоджуюсь.").FontSize(8).FontColor("#8e8e93");
                    });
                }
            });

            return document.GeneratePdf();
        }
        public byte[] GenerateTimesheetPdf(TimesheetFormDto data)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(15, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").FontColor(Colors.Black));

                    // 1. ШАПКА ДОКУМЕНТА (Текст і Логотип на одному рівні)
                    page.Header().PaddingBottom(15).Row(row =>
                    {
                        // Ліва частина (Текст)
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("ЗАРПЛАТНИЙ ТАБЕЛЬ").FontSize(22).Bold().FontColor(Colors.Grey.Darken3);
                            col.Item().Text($"Період: {data.Month ?? ""}").FontSize(12).FontColor(Colors.Grey.Medium);
                            col.Item().PaddingTop(5).Text(txt =>
                            {
                                txt.Span("Працівник: ").FontSize(12).FontColor(Colors.Grey.Medium);
                                txt.Span(string.IsNullOrWhiteSpace(data.FullName) ? "___________________" : data.FullName).FontSize(14).Bold();
                            });
                        });

                        // Права частина (Великий логотип)
                        if (System.IO.File.Exists("wwwroot/images/logo.png"))
                        {
                            // AutoItem() і Height(70) - безпечний метод, який гарантовано не викличе помилку
                            row.AutoItem().AlignRight().AlignTop().Height(70).Image("wwwroot/images/logo.png");
                        }
                    });

                    // 2. ТІЛО ДОКУМЕНТА
                    page.Content().Column(col =>
                    {
                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        col.Item().PaddingVertical(15).Row(row =>
                        {
                            // === ЛІВА КОЛОНКА (СТАТИСТИКА) ===
                            row.RelativeItem().PaddingRight(10).Column(leftCol =>
                            {
                                leftCol.Item().PaddingBottom(5).Text("1. СТАТИСТИКА РОБОТИ").Bold().FontSize(11).FontColor(Colors.Grey.Darken2);

                                leftCol.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(80); });

                                    table.Header(h =>
                                    {
                                        h.Cell().PaddingBottom(4).Text("Показник").SemiBold();
                                        h.Cell().PaddingBottom(4).AlignRight().Text("Кількість").SemiBold();
                                        h.Cell().ColumnSpan(2).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                                    });

                                    void AddStat(string label, string val, string colorCode = "")
                                    {
                                        table.Cell().PaddingVertical(6).Text(label);
                                        var txt = table.Cell().PaddingVertical(6).AlignRight().Text(val).Bold();
                                        if (!string.IsNullOrEmpty(colorCode)) txt.FontColor(colorCode);
                                        table.Cell().ColumnSpan(2).LineHorizontal(1).LineColor(Colors.Grey.Lighten4);
                                    }

                                    AddStat("Відпрацьовано днів", $"{data.WorkingDays}");
                                    AddStat("Запізнення на зміну", $"{data.Latenesses}", Colors.Red.Medium);
                                    AddStat("Продано iPhone", $"{data.IphonesSold} шт");
                                    AddStat("iPad / Watch / Mac", $"{data.OtherAppleSold} шт");
                                    AddStat("Продано гарантій", $"{data.WarrantiesSold} шт");
                                    AddStat("Оборот аксесуарів", $"{data.AccessoriesRevenueUAH:N0} ₴");
                                    AddStat("Надано послуг", $"{data.ServicesSold} шт");
                                });
                            });

                            // === ПРАВА КОЛОНКА (ГРОШІ) ===
                            row.RelativeItem().PaddingLeft(10).Column(rightCol =>
                            {
                                rightCol.Item().PaddingBottom(5).Text("2. ДЕТАЛІЗАЦІЯ НАРАХУВАНЬ").Bold().FontSize(11).FontColor(Colors.Grey.Darken2);

                                rightCol.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(80); });

                                    table.Header(h =>
                                    {
                                        h.Cell().PaddingBottom(4).Text("Стаття").SemiBold();
                                        h.Cell().PaddingBottom(4).AlignRight().Text("Сума (Грн)").SemiBold();
                                        h.Cell().ColumnSpan(2).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                                    });

                                    void AddMoney(string label, decimal val, string colorCode = "", bool isBold = false)
                                    {
                                        table.Cell().PaddingVertical(6).Text(label);
                                        var txt = table.Cell().PaddingVertical(6).AlignRight().Text($"{val:N2}");
                                        if (isBold) txt.Bold();
                                        if (!string.IsNullOrEmpty(colorCode)) txt.FontColor(colorCode);
                                        table.Cell().ColumnSpan(2).LineHorizontal(1).LineColor(Colors.Grey.Lighten4);
                                    }

                                    AddMoney("Базова ставка", data.BaseSalaryAmount);
                                    AddMoney("Бонус: iPhone", data.IphonesBonusAmount);
                                    AddMoney("Бонус: iPad/Watch/Mac", data.OtherAppleBonusAmount);
                                    AddMoney("Бонус: Гарантії", data.WarrantiesBonusAmount);
                                    AddMoney("Відсоток: Аксесуари", data.AccessoriesBonusAmount);
                                    AddMoney("Відсоток: Послуги", data.ServicesBonusAmount);
                                    AddMoney("Премія", data.BonusAmount, Colors.Blue.Medium, true);
                                    AddMoney("Штрафи / Утримання", -data.PenaltyAmount, Colors.Red.Medium, true);
                                });
                            });
                        });

                        // 3. БЛОК ПІДСУМКІВ
                        col.Item().PaddingTop(10).Row(r =>
                        {
                            r.RelativeItem(); // Порожній простір зліва

                            r.ConstantItem(280).Background(Colors.Grey.Lighten4).Padding(15).Column(box =>
                            {
                                box.Item().Row(subRow =>
                                {
                                    subRow.RelativeItem().Text("ВСЬОГО НАРАХОВАНО:").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken3);
                                    subRow.AutoItem().Text($"{data.TotalAccrued:N2} ₴").FontSize(14).Bold().FontColor(Colors.Black);
                                });

                                box.Item().PaddingVertical(8).Row(subRow =>
                                {
                                    subRow.RelativeItem().Text("Виданий аванс:").FontSize(11).FontColor(Colors.Grey.Darken2);
                                    subRow.AutoItem().Text($"- {data.AdvancePayment:N2} ₴").FontSize(11).FontColor(Colors.Grey.Darken2);
                                });

                                box.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                                box.Item().PaddingTop(8).Row(subRow =>
                                {
                                    subRow.RelativeItem().Text("ДО ВИПЛАТИ:").FontSize(14).Bold().FontColor(Colors.Black);
                                    subRow.AutoItem().Text($"{data.FinalPayout:N0} ₴").FontSize(16).Bold().FontColor(Colors.Green.Darken2);
                                });
                            });
                        });

                        // 4. ПІДПИСИ
                        col.Item().PaddingTop(50).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().LineHorizontal(1).LineColor(Colors.Black);
                                c.Item().PaddingTop(5).AlignCenter().Text("Підпис Керівника").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });

                            row.ConstantItem(50); // Простір між підписами

                            row.RelativeItem().Column(c =>
                            {
                                c.Item().LineHorizontal(1).LineColor(Colors.Black);
                                c.Item().PaddingTop(5).AlignCenter().Text("Підпис Працівника (з сумою згоден)").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });
                    });

                    // 5. ФУТЕР
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Згенеровано системою Apple Shopik CRM (").FontSize(8).FontColor(Colors.Grey.Lighten1);
                        x.Span($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8).FontColor(Colors.Grey.Lighten1);
                        x.Span(")").FontSize(8).FontColor(Colors.Grey.Lighten1);
                    });
                });
            });

            return document.GeneratePdf();
        }
    }
}
