namespace KamPay.Models;

// Rozet (Badge) modeli - Oyunlaþtýrma için
public class Badge
{
    public string BadgeId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string IconName { get; set; }
    public BadgeCategory Category { get; set; }
    public int RequiredPoints { get; set; }
    public int RequiredCount { get; set; } // Örn: 10 ürün sat
    public string Color { get; set; }

    public Badge()
    {
        BadgeId = Guid.NewGuid().ToString();
    }

    // Varsayýlan rozetler
    public static List<Badge> GetDefaultBadges()
    {
        return new List<Badge>
            {
                new Badge
                {
                    Name = "Ýlk Adým",
                    Description = "Ýlk ürününü ekledin!",
                    IconName = "badge_first.png",
                    Category = BadgeCategory.Seller,
                    RequiredCount = 1,
                    Color = "#4CAF50"
                },
                new Badge
                {
                    Name = "Paylaþým Kahramaný",
                    Description = "5 ürün paylaþtýn",
                    IconName = "badge_hero.png",
                    Category = BadgeCategory.Seller,
                    RequiredCount = 5,
                    Color = "#2196F3"
                },
                new Badge
                {
                    Name = "Baðýþ Meleði",
                    Description = "3 ürün baðýþladýn",
                    IconName = "badge_angel.png",
                    Category = BadgeCategory.Donation,
                    RequiredCount = 3,
                    Color = "#FF9800"
                },
                new Badge
                {
                    Name = "Aktif Alýcý",
                    Description = "5 ürün aldýn",
                    IconName = "badge_buyer.png",
                    Category = BadgeCategory.Buyer,
                    RequiredCount = 5,
                    Color = "#9C27B0"
                },
                new Badge
                {
                    Name = "Süper Satýcý",
                    Description = "10 ürün sattýn",
                    IconName = "badge_super_seller.png",
                    Category = BadgeCategory.Seller,
                    RequiredCount = 10,
                    Color = "#FF5722"
                },
                new Badge
                {
                    Name = "Kampüs Yýldýzý",
                    Description = "100 puana ulaþtýn",
                    IconName = "badge_star.png",
                    Category = BadgeCategory.Points,
                    RequiredPoints = 100,
                    Color = "#FFC107"
                }
            };
    }
}

public enum BadgeCategory
{
    Seller = 0,    // Satýcý rozetleri
    Buyer = 1,     // Alýcý rozetleri
    Donation = 2,  // Baðýþ rozetleri
    Points = 3,    // Puan rozetleri
    Special = 4    // Özel rozetler
}

// Kullanýcý rozeti (User'ýn kazandýðý rozetler)
public class UserBadge
{
    public string UserBadgeId { get; set; }
    public string UserId { get; set; }
    public string BadgeId { get; set; }
    public DateTime EarnedAt { get; set; }

    // Badge bilgileri (cache için)
    public string BadgeName { get; set; }
    public string BadgeIcon { get; set; }
    public string BadgeColor { get; set; }

    public UserBadge()
    {
        UserBadgeId = Guid.NewGuid().ToString();
        EarnedAt = DateTime.UtcNow;
    }
}