using System.Collections.Generic;

namespace AppleShopikCRM.Client.Models // Або ваш простір імен
{
    public static class AppleData
    {
        public static readonly Dictionary<string, (List<string> Colors, List<string> Memories)> Library = new()
        {
            { "iPhone 17 Pro Max", (
                new() { "Cosmic Orange", "Silver", "Deep Blue" },
                new() { "256GB", "512GB", "1TB", "2TB" }
            )},
            { "iPhone 17 Pro", (
                new() { "Cosmic Orange", "Silver", "Deep Blue" },
                new() { "256GB", "512GB", "1TB" }
            )},
            { "iPhone 17", (
                new() { "Sage", "Black", "White", "Mist Blue", "Levander" },
                new() { "256GB", "512GB", "1TB" }
            )},
            { "iPhone 17 Air", (
                new() { "Light Gold", "Space Black", "Could White", "Sky Blue" },
                new() { "256GB", "512GB", "1TB" }
            )},
             { "iPhone 16 Pro Max", (
                new() { "Natural Titanium", "Black Titanium", "White Titanium", "Desert Titanium" },
                new() { "256GB", "512GB", "1TB" }
            )},
            { "iPhone 16 Pro", (
                new() { "Natural Titanium", "Black Titanium", "White Titanium", "Desert Titanium" },
                new() { "128GB", "256GB", "512GB", "1TB" }
            )},
            { "iPhone 16", (
                new() { "Ultramarine", "Black", "White", "Teal", "Pink" },
                new() { "128GB", "256GB", "512GB"}
            )},
            { "iPhone 16 Plus", (
                new() { "Ultramarine", "Black", "White", "Teal", "Pink" },
                new() { "128GB", "256GB", "512GB"}
            )},
            { "iPhone 15 Pro Max", (
                new() { "Natural Titanium", "Black Titanium", "White Titanium", "Blue Titanium" },
                new() { "256GB", "512GB", "1TB" }
            )},
            { "iPhone 15 Pro", (
                new() { "Natural Titanium", "Black Titanium", "White Titanium", "Blue Titanium" },
                new() { "128GB", "256GB", "512GB", "1TB" }
            )},
            { "iPhone 15", (
                new() { "Black", "Blue", "Green", "Yellow", "Pink" },
                new() { "128GB", "256GB", "512GB" }
            )},
            { "iPhone 15 Plus", (
                new() { "Black", "Blue", "Green", "Yellow", "Pink" },
                new() { "128GB", "256GB", "512GB" }
            )},
            { "iPhone 14 Pro Max", (
                new() { "Space Black", "Silver", "Gold", "Deep Purple" },
                new() { "128GB", "256GB", "512GB", "1TB" }
            )},
            { "iPhone 14 Pro", (
                new() { "Space Black", "Silver", "Gold", "Deep Purple" },
                new() { "128GB", "256GB", "512GB", "1TB" }
            )},
            { "iPhone 14", (
                new() { "Midnight", "Starlight", "Blue", "Purple", "Red" },
                new() { "128GB", "256GB", "512GB" }
            )},
            { "iPhone 14 Plus", (
                new() { "Midnight", "Starlight", "Blue", "Purple", "Red" },
                new() { "128GB", "256GB", "512GB" }
            )},
            { "iPhone 13 Pro Max", (
                new() { "Graphite", "Gold", "Silver", "Sierra Blue", "Alphine Green" },
                new() { "128GB", "256GB", "512GB", "1TB" }
            )},
            { "iPhone 13 Pro", (
                new() { "Graphite", "Gold", "Silver", "Sierra Blue", "Alphine Green" },
                new() { "128GB", "256GB", "512GB", "1TB" }
            )},
            { "iPhone 13", (
                new() { "Starlight", "Midnight", "Blue", "Pink", "Red", "Green" },
                new() { "128GB", "256GB", "512GB" }
            )},
            { "iPhone 13 Mini", (
                new() { "Starlight", "Midnight", "Blue", "Pink", "Red", "Green" },
                new() { "128GB", "256GB", "512GB" }
            )},
            { "iPhone 12 Pro Max", (
                new() { "Graphite", "Gold", "Silver", "Pacific Blue" },
                new() { "128GB", "256GB", "512GB" }
            )},
            { "iPhone 12 Pro", (
                new() { "Graphite", "Gold", "Silver", "Pacific Blue" },
                new() { "128GB", "256GB", "512GB" }
            )},
            { "iPhone 12", (
                new() { "White", "Black", "Purple", "Blue", "Red", "Green" },
                new() { "64GB", "128GB", "256GB" }
            )},
            { "iPhone 12 Mini", (
                new() { "White", "Black", "Purple", "Blue", "Red", "Green" },
                new() { "64GB", "128GB", "256GB" }
            )},
            { "iPhone 11 Pro Max", (
                new() { "Space Gray", "Gold", "Silver", "Midnight Green" },
                new() { "64GB", "256GB", "512GB" }
            )},
            { "iPhone 11 Pro", (
                 new() { "Space Gray", "Gold", "Silver", "Midnight Green" },
                 new() { "64GB", "256GB", "512GB" }
            )},
            { "iPhone 11", (
                new() { "White", "Black", "Yellow", "Purple", "Red", "Green" },
                new() { "64GB", "128GB", "256GB" }
            )},
            { "iPhone Xs", (
                new() { "Space Gray", "Gold", "Silver" },
                new() { "64GB", "256GB", "512GB" }
            )},
            { "iPhone Xs Max", (
                new() { "Space Gray", "Gold", "Silver" },
                new() { "64GB", "256GB", "512GB" }
            )},
            { "iPhone Xr", (
                new() { "White", "Black", "Yellow", "Blue", "Red", "Coral" },
                new() { "64GB", "128GB", "256GB" }
            )},
            { "iPhone X", (
                new() { "Space Gray", "Silver" },
                new() { "64GB", "256GB" }
            )}
        };
    }
}