using System.Net.Http.Headers;
using Blazored.LocalStorage;

public class JwtAuthHandler : DelegatingHandler
{
    private readonly ILocalStorageService _localStorage;

    public JwtAuthHandler(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[JwtAuthHandler] Intercepting request to: {request.RequestUri}");
        // 1. Дістаємо токен з пам'яті
        var token = await _localStorage.GetItemAsync<string>("authToken");

        // 2. Якщо токен є — додаємо його в заголовок Authorization
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // 3. Відправляємо запит далі
        return await base.SendAsync(request, cancellationToken);
    }
}
