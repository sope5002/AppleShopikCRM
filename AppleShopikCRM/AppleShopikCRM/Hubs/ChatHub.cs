using Microsoft.AspNetCore.SignalR;

namespace AppleShopikCRM.Hubs
{
    // Це "хаб" - точка підключення для нашого Blazor-фронтенду
    public class ChatHub : Hub
    {
        // Поки що залишаємо пустим. 
        // Бекенд буде використовувати його, щоб "пушити" дані на клієнт.
        public async Task JoinStoreGroup(int storeId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Store_{storeId}");
        }
    }

}