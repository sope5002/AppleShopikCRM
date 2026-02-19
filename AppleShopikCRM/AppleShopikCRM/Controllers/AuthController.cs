using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AppleShopikCRM.Server.Models;
using AppleShopikCRM.Client.Models;
using Microsoft.AspNetCore.Authorization; // DTO для логіну (створимо нижче)

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly RoleManager<IdentityRole> _roleManager; // <--- ДОДАЛИ

    public AuthController(UserManager<ApplicationUser> userManager,
                          SignInManager<ApplicationUser> signInManager,
                          RoleManager<IdentityRole> roleManager,
                          IConfiguration configuration)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _roleManager = roleManager;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto model)
    {
        var user = await _userManager.FindByNameAsync(model.Email); // Шукаємо по Email як Username
        if (user != null && await _userManager.CheckPasswordAsync(user, model.Password))
        {
            // Отримуємо ролі
            var userRoles = await _userManager.GetRolesAsync(user);

            // Формуємо "паспорт" (Claims)
            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.UserName!),
                new Claim(ClaimTypes.UserData, user.FullName!),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("StoreId", user.StoreId?.ToString() ?? "0"), // ЗАШИВАЄМО МАГАЗИН!
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            foreach (var role in userRoles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, role));
            }


            var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                expires: DateTime.Now.AddHours(12),
                claims: authClaims,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
            );

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                expiration = token.ValidTo
            });
        }
        return Unauthorized();
    }

    // Тимчасовий метод для створення першого адміна (запустиш через Swagger)
    [HttpPost("register-admin")]
    public async Task<IActionResult> RegisterAdmin()
    {
        if (await _userManager.FindByNameAsync("admin@shop.com") != null) return BadRequest("Admin exists");

        var user = new ApplicationUser { UserName = "admin@shop.com", Email = "admin@shop.com", FullName = "Головний Адмін", StoreId = null };
        var result = await _userManager.CreateAsync(user, "AdminPass123!"); // Пароль

        if (result.Succeeded) await _userManager.AddToRoleAsync(user, "Admin");

        return Ok("Admin created");
    }
    [HttpPost("register")]
    [Authorize(Roles = "Admin")] // <--- Розкоментуйте це пізніше, щоб тільки Адмін міг створювати людей
    public async Task<IActionResult> Register([FromBody] RegisterDto model)
    {
        // 1. Перевіряємо, чи існує такий email
        var userExists = await _userManager.FindByNameAsync(model.Email);
        if (userExists != null)
            return BadRequest("Користувач з таким Email вже існує!");

        // 2. Створюємо об'єкт користувача
        ApplicationUser user = new()
        {
            Email = model.Email,
            SecurityStamp = Guid.NewGuid().ToString(),
            UserName = model.Email,
            FullName = model.FullName,
            StoreId = model.StoreId
        };

        // 3. Зберігаємо в базу (Identity сама захешує пароль)
        var result = await _userManager.CreateAsync(user, model.Password);

        if (!result.Succeeded)
            return BadRequest($"Помилка створення: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        // 4. Перевіряємо, чи існує така роль, якщо ні - створюємо
        // (Для цього треба додати RoleManager у конструктор, див. нижче)
        if (!await _roleManager.RoleExistsAsync(model.Role))
            await _roleManager.CreateAsync(new IdentityRole(model.Role));

        // 5. Призначаємо роль
        if (await _roleManager.RoleExistsAsync(model.Role))
        {
            await _userManager.AddToRoleAsync(user, model.Role);
        }

        return Ok(new { Status = "Success", Message = "Користувача створено успішно!" });
    }
}