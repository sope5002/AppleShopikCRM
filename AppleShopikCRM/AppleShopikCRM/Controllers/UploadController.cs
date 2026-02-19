using Microsoft.AspNetCore.Mvc;
using AppleShopikCRM.Server.Services; // Ваш простір імен для сервісів

namespace AppleShopikCRM.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UploadController : ControllerBase
    {
        private readonly StorageService _storageService;

        public UploadController(StorageService storageService)
        {
            _storageService = storageService;
        }

        [HttpPost("product-image")]
        public async Task<IActionResult> UploadProductImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Файл не вибрано");

            // Перевіряємо формат
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".webp")
                return BadRequest("Дозволені лише формати JPG, PNG або WEBP");

            try
            {
                // Перетворюємо файл у масив байтів
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();

                // Відправляємо на FTP і отримуємо URL
                string imageUrl = await _storageService.UploadProductImageAsync(fileBytes, ext);

                // Повертаємо URL клієнту
                return Ok(new { Url = imageUrl });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FTP Upload Error: {ex.Message}");
                return StatusCode(500, "Помилка при завантаженні файлу на FTP");
            }
        }
    }
}