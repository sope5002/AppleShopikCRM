using System;

namespace AppleShopikCRM.Client.Helpers
{
    public static class MenuNotifier
    {
        public static event Action? OnUpdate;
        public static void Trigger() => OnUpdate?.Invoke();
    }
}