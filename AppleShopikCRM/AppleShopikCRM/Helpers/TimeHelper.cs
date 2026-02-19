using System;

namespace AppleShopikCRM.Helpers // Замініть на ваш namespace
{
    public static class TimeHelper
    {
        public static DateTime GetUkraineTime()
        {
            var utcNow = DateTime.UtcNow;

            try
            {
                // Спочатку пробуємо знайти часовий пояс для Linux-серверів (часто використовується на хостингах)
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
                return TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
            }
            catch (TimeZoneNotFoundException)
            {
                // Якщо сервер на Windows (наприклад SmarterASP.net або інші Windows-хостинги)
                try
                {
                    TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
                    // FLE Standard Time = Київ, Рига, Софія, Таллінн, Вільнюс
                    return TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
                }
                catch
                {
                    // Запасний варіант, якщо сервер взагалі не розуміє часових поясів (просто додаємо +2 години)
                    return utcNow.AddHours(2); // Взимку +2, влітку треба буде +3, але перші два try спрацюють у 99.9% випадків
                }
            }
        }
    }
}