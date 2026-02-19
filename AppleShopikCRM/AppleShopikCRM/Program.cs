using AppleShopikCRM;
using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Client.Components; // <-- Тут має бути ваш App.razor (перевірте namespace)
using AppleShopikCRM.Server.Models;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json.Serialization;
using AppleShopikCRM.Components;

var builder = WebApplication.CreateBuilder(args);

// 1. ПІДКЛЮЧЕННЯ БАЗИ ДАНИ
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseMySql(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))
        ));


// 2. IDENTITY
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 4;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
 .AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// 3. JWT АВТЕНТИФІКАЦІЯ
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});

// 4. СЕРВІСИ BLAZOR (.NET 8)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options => { options.DetailedErrors = true; });

builder.Services.AddBlazoredLocalStorage();

builder.Services.AddScoped(sp => new HttpClient
{
    // Вкажіть тут адресу вашого сайту (яку ви бачите в браузері)
    // Це потрібно, щоб Сервер не падав при спробі створити сторінку Login
    BaseAddress = new Uri("https://localhost:7080/")
});
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // 🔥 ЦЕЙ РЯДОК ВИРІШУЄ ПРОБЛЕМУ ЗАЦИКЛЕННЯ
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddRazorPages();

// 5. SWAGGER
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AppleShopik API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
    {
        new OpenApiSecurityScheme {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        },
        new string[] {}
    }});
});
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // 👇 ЦЕЙ РЯДОК ДОЗВОЛЯЄ СЕРВЕРУ ВІДПРАВЛЯТИ СКЛАДНІ ОБ'ЄКТИ БЕЗ ПОМИЛОК
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddScoped<AppleShopikCRM.Server.Services.PdfService>();
builder.Services.AddScoped<AppleShopikCRM.Server.Services.StorageService>();

var app = builder.Build();

// --- HTTP PIPELINE ---

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery(); // Важливо для .NET 8

// 6. АВТОРИЗАЦІЯ
app.UseAuthentication();
app.UseAuthorization();

// 7. МАРШРУТИЗАЦІЯ
app.MapControllers(); // Вмикає API (api/sales, api/auth)

// ВАЖЛИВО: Запуск App.razor замість index.html
app.MapRazorComponents<App>() // Переконайтеся, що клас App доступний (додайте using)
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(AppleShopikCRM.Client._Imports).Assembly); // Підключаємо Client збірку

app.Run();