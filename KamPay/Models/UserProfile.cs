/*using System;
using System.Collections.Generic;
using System.Linq;

namespace KamPay.Models
{
    /// <summary>
    /// Genişletilmiş kullanıcı profil modeli
    /// Temel User sınıfına ek olarak detaylı profil bilgileri
    /// </summary>
    public class UserProfile
    {
        // Temel Bilgiler
        public string UserId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string ProfileImageUrl { get; set; }
        
        // Biyografi
        public string Bio { get; set; }
        public string University { get; set; }
        public string Department { get; set; }
        public int? GraduationYear { get; set; }
        
        // Konum Bilgileri
        public string Campus { get; set; }
        public string Building { get; set; }
        public string RoomNumber { get; set; }
        
        // İstatistikler
        public int TotalProducts { get; set; }
        public int ActiveProducts { get; set; }
        public int SoldProducts { get; set; }
        public int DonatedProducts { get; set; }
        public int PurchasedProducts { get; set; }
        public int TotalViews { get; set; }
        public int TotalFavorites { get; set; }
        
        // Puanlama Sistemi
        public int DonationPoints { get; set; }
        public int TrustScore { get; set; }
        public double AverageRating { get; set; }
        public int TotalRatings { get; set; }
        
        // Rozetler
        public List<string> BadgeIds { get; set; }
        public int TotalBadges => BadgeIds?.Count ?? 0;
        
        // Tercihler
        public bool ShowEmail { get; set; }
        public bool ShowPhoneNumber { get; set; }
        public bool AllowMessages { get; set; }
        public bool EmailNotifications { get; set; }
        public bool PushNotifications { get; set; }
        
        // Tarihler
        public DateTime MemberSince { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        
        // Durum
        public bool IsActive { get; set; }
        public bool IsVerified { get; set; }
        public bool IsPremium { get; set; }
        
        // Sosyal Bağlantılar
        public string InstagramHandle { get; set; }
        public string TwitterHandle { get; set; }
        public string LinkedInUrl { get; set; }
        
        public UserProfile()
        {
            UserId = Guid.NewGuid().ToString();
            MemberSince = DateTime.UtcNow;
            IsActive = true;
            IsVerified = false;
            IsPremium = false;
            TrustScore = 100;
            DonationPoints = 0;
            BadgeIds = new List<string>();
            
            // Varsayılan gizlilik ayarları
            ShowEmail = false;
            ShowPhoneNumber = false;
            AllowMessages = true;
            EmailNotifications = true;
            PushNotifications = true;
        }
        
        /// <summary>
        /// Tam ad döndürür
        /// </summary>
        public string FullName => $"{FirstName} {LastName}";
        
        /// <summary>
        /// Kullanıcı aktif mi?
        /// </summary>
        public bool IsOnline
        {
            get
            {
                if (!LastActivityAt.HasValue)
                    return false;
                
                var diff = DateTime.UtcNow - LastActivityAt.Value;
                return diff.TotalMinutes < 15; // Son 15 dakikada aktifse online
            }
        }
        
        /// <summary>
        /// Son görülme zamanı metni
        /// </summary>
        public string LastSeenText
        {
            get
            {
                if (IsOnline)
                    return "Çevrimiçi";
                
                if (!LastActivityAt.HasValue)
                    return "Hiç görülmedi";
                
                var diff = DateTime.UtcNow - LastActivityAt.Value;
                
                if (diff.TotalMinutes < 60)
                    return $"{(int)diff.TotalMinutes} dakika önce";
                if (diff.TotalHours < 24)
                    return $"{(int)diff.TotalHours} saat önce";
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays} gün önce";
                
                return LastActivityAt.Value.ToString("dd MMM yyyy");
            }
        }
        
        /// <summary>
        /// Üyelik süresi (gün)
        /// </summary>
        public int MembershipDays => (int)(DateTime.UtcNow - MemberSince).TotalDays;
        
        /// <summary>
        /// Profil tamamlanma yüzdesi
        /// </summary>
        public int ProfileCompleteness
        {
            get
            {
                int score = 0;
                int maxScore = 100;
                
                // Temel bilgiler (40 puan)
                if (!string.IsNullOrEmpty(FirstName)) score += 5;
                if (!string.IsNullOrEmpty(LastName)) score += 5;
                if (!string.IsNullOrEmpty(Email)) score += 10;
                if (!string.IsNullOrEmpty(PhoneNumber)) score += 10;
                if (!string.IsNullOrEmpty(ProfileImageUrl)) score += 10;
                
                // Biyografi (20 puan)
                if (!string.IsNullOrEmpty(Bio)) score += 10;
                if (!string.IsNullOrEmpty(University)) score += 5;
                if (!string.IsNullOrEmpty(Department)) score += 5;
                
                // Konum (15 puan)
                if (!string.IsNullOrEmpty(Campus)) score += 5;
                if (!string.IsNullOrEmpty(Building)) score += 5;
                if (!string.IsNullOrEmpty(RoomNumber)) score += 5;
                
                // Aktivite (25 puan)
                if (TotalProducts > 0) score += 10;
                if (DonationPoints > 0) score += 10;
                if (BadgeIds.Any()) score += 5;
                
                return (int)((score / (double)maxScore) * 100);
            }
        }
        
        /// <summary>
        /// Güvenilirlik seviyesi
        /// </summary>
        public string TrustLevel
        {
            get
            {
                if (TrustScore >= 90) return "Çok Güvenilir";
                if (TrustScore >= 70) return "Güvenilir";
                if (TrustScore >= 50) return "Orta";
                if (TrustScore >= 30) return "Düşük";
                return "Çok Düşük";
            }
        }
        
        /// <summary>
        /// Aktivite seviyesi
        /// </summary>
        public string ActivityLevel
        {
            get
            {
                var totalActivity = TotalProducts + DonatedProducts + PurchasedProducts;
                
                if (totalActivity >= 50) return "Çok Aktif";
                if (totalActivity >= 20) return "Aktif";
                if (totalActivity >= 5) return "Orta";
                if (totalActivity > 0) return "Düşük";
                return "Yeni Üye";
            }
        }
        
        /// <summary>
        /// Rozet emoji'lerini döndürür
        /// </summary>
        public string GetBadgeEmojis(int limit = 3)
        {
            if (!BadgeIds.Any())
                return "";
            
            // İlk N rozet için emoji
            var emojis = new[] { "🥇", "🥈", "🥉", "🏅", "⭐", "🌟" };
            var count = Math.Min(BadgeIds.Count, limit);
            
            return string.Join(" ", emojis.Take(count));
        }
        
        /// <summary>
        /// Kullanıcının öne çıkan özelliği
        /// </summary>
        public string HighlightFeature
        {
            get
            {
                if (DonatedProducts >= 10)
                    return $"💝 {DonatedProducts} bağış yaptı";
                if (TotalProducts >= 20)
                    return $"📦 {TotalProducts} ürün paylaştı";
                if (TrustScore >= 90)
                    return $"⭐ {TrustScore} güven puanı";
                if (DonationPoints >= 100)
                    return $"🎯 {DonationPoints} puan kazandı";
                if (MembershipDays >= 90)
                    return $"📅 {MembershipDays / 30} aydır üye";
                
                return "🆕 Yeni üye";
            }
        }
        
        /// <summary>
        /// User modelinden UserProfile oluşturur
        /// </summary>
        public static UserProfile FromUser(User user)
        {
            return new UserProfile
            {
                UserId = user.UserId,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                ProfileImageUrl = user.ProfileImageUrl,
                MemberSince = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsActive = user.IsActive,
                IsVerified = user.IsEmailVerified,
                TrustScore = user.TrustScore,
                DonationPoints = user.DonationPoints
            };
        }
    }
    
    /// <summary>
    /// Kullanıcı değerlendirmesi
    /// </summary>
    public class UserRating
    {
        public string RatingId { get; set; }
        public string RatedUserId { get; set; }
        public string RaterUserId { get; set; }
        public string RaterName { get; set; }
        public int Score { get; set; } // 1-5
        public string Comment { get; set; }
        public string ProductId { get; set; }
        public DateTime CreatedAt { get; set; }
        
        public UserRating()
        {
            RatingId = Guid.NewGuid().ToString();
            CreatedAt = DateTime.UtcNow;
        }
        
        public string ScoreStars => new string('⭐', Score);
    }
}
 * */