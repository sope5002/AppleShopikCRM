using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AppleShopikCRM.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CustomAuthStateProvider>();

// 👇 2. Кажемо системі: "Коли хтось просить AuthenticationStateProvider — дай їм ТОЙ САМИЙ, що зверху"
builder.Services.AddScoped<AuthenticationStateProvider>(provider =>
    provider.GetRequiredService<CustomAuthStateProvider>());

// === РЕЄСТРАЦІЯ HTTP CLIENT З ТОКЕНОМ ===

// 1. Реєструємо хендлер
builder.Services.AddTransient<JwtAuthHandler>();

// 2. Налаштовуємо іменований клієнт з хендлером
builder.Services.AddHttpClient("AppleShopikAPI", client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
})
.AddHttpMessageHandler<JwtAuthHandler>(); // <--- Це додає токен!

// 3. Підміняємо стандартний HttpClient на наш "прокачаний"
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("AppleShopikAPI"));

// =========================================

await builder.Build().RunAsync();