using System;
using System.Collections.Generic;
/*product.csde tanımlı enum
 * SİLİNECEK
namespace KamPay.Models
{
    /// <summary>
    /// Ürün kategorileri için model sınıfı
    /// </summary>
    public class Category
    {
        public string CategoryId { get; set; }
        public string Name { get; set; }
        public string IconName { get; set; }
        public string Description { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }

        public Category()
        {
            CategoryId = Guid.NewGuid().ToString();
            IsActive = true;
            DisplayOrder = 0;
        }

        /// <summary>
        /// Varsayılan kategorileri döndürür
        /// </summary>
        public static List<Category> GetDefaultCategories()
        {
            return new List<Category>
            {
                new Category
                {
                    Name = "Elektronik",
                    IconName = "laptop.png",
                    Description = "Bilgisayar, telefon, tablet, kulaklık vb.",
                    DisplayOrder = 1
                },
                new Category
                {
                    Name = "Kitap ve Kırtasiye",
                    IconName = "book.png",
                    Description = "Ders kitapları, romanlar, defterler, kalemler",
                    DisplayOrder = 2
                },
                new Category
                {
                    Name = "Giyim",
                    IconName = "tshirt.png",
                    Description = "Kıyafet, ayakkabı, çanta, aksesuar",
                    DisplayOrder = 3
                },
                new Category
                {
                    Name = "Ev Eşyası",
                    IconName = "home.png",
                    Description = "Mobilya, dekorasyon, mutfak eşyaları",
                    DisplayOrder = 4
                },
                new Category
                {
                    Name = "Spor Malzemeleri",
                    IconName = "dumbbell.png",
                    Description = "Spor ekipmanları, kamp malzemeleri, bisiklet",
                    DisplayOrder = 5
                },
                new Category
                {
                    Name = "Müzik Aletleri",
                    IconName = "guitar.png",
                    Description = "Gitar, piyano, davul ve aksesuarları",
                    DisplayOrder = 6
                },
                new Category
                {
                    Name = "Oyun ve Hobi",
                    IconName = "gamepad.png",
                    Description = "Oyun konsolu, board game, puzzle, hobi malzemeleri",
                    DisplayOrder = 7
                },
                new Category
                {
                    Name = "Bebek Ürünleri",
                    IconName = "baby.png",
                    Description = "Bebek kıyafeti, oyuncak, bebek arabaları",
                    DisplayOrder = 8
                },
                new Category
                {
                    Name = "Diğer",
                    IconName = "grid.png",
                    Description = "Diğer kategorilere uymayan ürünler",
                    DisplayOrder = 9
                }
            };
        }

        /// <summary>
        /// Kategori ID'sine göre kategori ismi döndürür
        /// </summary>
        public static string GetCategoryName(string categoryId)
        {
            var categories = GetDefaultCategories();
            var category = categories.Find(c => c.CategoryId == categoryId);
            return category?.Name ?? "Belirtilmemiş";
        }

        /// <summary>
        /// Kategori isminden ID'yi bulur
        /// </summary>
        public static string GetCategoryIdByName(string name)
        {
            var categories = GetDefaultCategories();
            var category = categories.Find(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return category?.CategoryId ?? string.Empty;
        }
    }
} 
*/