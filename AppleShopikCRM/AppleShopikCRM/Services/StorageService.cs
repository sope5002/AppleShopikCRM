using FluentFTP;
using Microsoft.Extensions.Options;

namespace AppleShopikCRM.Server.Services
{
    public class StorageService
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public StorageService(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        public async Task<string> SaveReceiptAsync(byte[] fileData, int orderId, string shopName)
        {
            string dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{shopName}__{orderId}_{dateStr}.pdf";

            // 1. Збереження ЛОКАЛЬНО (на сервері)
            // Шлях: wwwroot/reports/PDFChek/
            string webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            string localFolder = Path.Combine(webRoot, "reports", "PDFChek");

            if (!Directory.Exists(localFolder)) Directory.CreateDirectory(localFolder);

            string localPath = Path.Combine(localFolder, fileName);
            await File.WriteAllBytesAsync(localPath, fileData);

            // 2. Збереження на FTP (Хмара)
            try
            {
                var ftpHost = _config["FtpSettings:Host"];
                var ftpUser = _config["FtpSettings:User"];
                var ftpPass = _config["FtpSettings:Password"];

                // З'єднуємося
                using var ftp = new AsyncFtpClient(ftpHost, ftpUser, ftpPass);
                await ftp.AutoConnect();

                // Завантажуємо
                // remotePath буде: /shopName/filename.pdf
                string remotePath = $"/{shopName}/{fileName}";

                // Перевіряємо чи є папка магазину на FTP
                if (!await ftp.DirectoryExists($"/{shopName}"))
                {
                    await ftp.CreateDirectory($"/{shopName}");
                }

                await ftp.UploadBytes(fileData, remotePath);
                await ftp.Disconnect();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FTP Error: {ex.Message}");
                // Не кидаємо помилку, щоб не ламати роботу каси, якщо інтернет поганий
            }

            return localPath; // Повертаємо шлях, де зберегли
        }
    }
}
