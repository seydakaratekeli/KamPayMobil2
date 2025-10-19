using KamPay.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KamPay.Services
{
    // Bu enum'un Models klasöründe veya uygun bir yerde tanýmlý olduðundan emin ol.
    // Eðer deðilse, bu enum'u projenize ekleyin.
    public enum UserAction
    {
        AddProduct,
        CompleteTransaction,
        MakeDonation,
        ReceiveDonation,
        ShareService,
        WriteReview
    }

    public interface IUserProfileService
    {
        // --- Profil Metotlarý ---

        /// <summary>
        /// Yeni bir kullanýcý için veritabanýnda profil ve baþlangýç istatistiklerini oluþturur.
        /// </summary>
        Task<ServiceResult<bool>> CreateUserProfileAsync(string userId, string username, string email);

        /// <summary>
        /// Belirtilen kullanýcýnýn genel profil bilgilerini (isim, resim vb.) getirir.
        /// </summary>
        Task<ServiceResult<UserProfile>> GetUserProfileAsync(string userId);


        // --- Ýstatistik ve Oyunlaþtýrma Metotlarý ---

        /// <summary>
        /// Belirtilen kullanýcýnýn istatistiklerini (puan, takas sayýsý vb.) getirir.
        /// </summary>
        Task<ServiceResult<UserStats>> GetUserStatsAsync(string userId);

        /// <summary>
        /// Belirli bir aksiyon için kullanýcýya standart puan ekler.
        /// </summary>
        Task<ServiceResult<bool>> AddPointsForAction(string userId, UserAction action);

        /// <summary>
        /// Belirtilen sebeple kullanýcýya belirli bir miktar puan ekler.
        /// </summary>
        Task<ServiceResult<bool>> AddPointsAsync(string userId, int points, string reason);

        /// <summary>
        /// Kullanýcýnýn mevcut istatistiklerine göre yeni rozetler kazanýp kazanmadýðýný kontrol eder ve gerekirse verir.
        /// </summary>
        Task<ServiceResult<bool>> CheckAndAwardBadgesAsync(string userId);

        /// <summary>
        /// Kullanýcýnýn sahip olduðu tüm rozetleri listeler.
        /// </summary>
        Task<ServiceResult<List<UserBadge>>> GetUserBadgesAsync(string userId);

        /// <summary>
        /// Kullanýcýya belirli bir rozeti manuel olarak verir.
        /// </summary>
        Task<ServiceResult<UserBadge>> AwardBadgeAsync(string userId, string badgeId);

        Task<ServiceResult<bool>> TransferTimeCreditsAsync(string fromUserId, string toUserId, int amount, string reason);

        // Not: UpdateUserStatsAsync metodu, puan/rozet ekleme iþlemleri tarafýndan dolaylý olarak
        // kullanýldýðý için genellikle doðrudan çaðrýlmaz. Ýhtiyaç halinde kullanýlabilir.
        Task<ServiceResult<bool>> UpdateUserStatsAsync(UserStats stats);
    
    
    
    }
}