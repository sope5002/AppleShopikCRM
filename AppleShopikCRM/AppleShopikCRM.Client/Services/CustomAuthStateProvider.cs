using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AppleShopikCRM.Client
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ILocalStorageService _localStorage;
        private readonly HttpClient _http;

        public CustomAuthStateProvider(ILocalStorageService localStorage, HttpClient http)
        {
            _localStorage = localStorage;
            _http = http;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var token = await _localStorage.GetItemAsync<string>("authToken");

            if (string.IsNullOrWhiteSpace(token))
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 👇 ВИКЛИК НОВОГО МЕТОДУ СТВОРЕННЯ
            return new AuthenticationState(new ClaimsPrincipal(CreateIdentityFromToken(token)));
        }

        public void MarkUserAsAuthenticated(string token)
        {
            // 👇 ВИКЛИК НОВОГО МЕТОДУ СТВОРЕННЯ
            var authUser = new ClaimsPrincipal(CreateIdentityFromToken(token));
            var authState = Task.FromResult(new AuthenticationState(authUser));
            NotifyAuthenticationStateChanged(authState);
        }

        public void MarkUserAsLoggedOut()
        {
            var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
            var authState = Task.FromResult(new AuthenticationState(anonymousUser));
            NotifyAuthenticationStateChanged(authState);
        }

        // 👇 НОВИЙ ДОПОМІЖНИЙ МЕТОД ДЛЯ ПРАВИЛЬНОЇ ПРИВ'ЯЗКИ
        private ClaimsIdentity CreateIdentityFromToken(string token)
        {
            var claims = ParseClaimsFromJwt(token);

            // Вказуємо мапінг для Name і Role, щоб AuthorizeView їх бачив
            return new ClaimsIdentity(claims, "jwt",
               nameType: "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
               roleType: "http://schemas.microsoft.com/ws/2008/06/identity/claims/role");
        }

        private IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var claims = new List<Claim>();
            var payload = jwt.Split('.')[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);

            foreach (var kvp in keyValuePairs)
            {
                if (kvp.Value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        claims.Add(new Claim(kvp.Key, item.ToString()));
                    }
                }
                else
                {
                    claims.Add(new Claim(kvp.Key, kvp.Value.ToString()));
                }
            }
            return claims;
        }

        private byte[] ParseBase64WithoutPadding(string base64)
        {
            // 1. Повертаємо базові символи Base64 замість URL-safe
            base64 = base64.Replace('-', '+').Replace('_', '/');

            // 2. Додаємо вирівнювання (padding), якого не вистачає
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            return Convert.FromBase64String(base64);
        }
    }
}