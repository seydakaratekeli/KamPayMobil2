using KamPay.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KamPay.Services
{
    // Bu enum'un Models klas�r�nde veya uygun bir yerde tan�ml� oldu�undan emin ol.
    // E�er de�ilse, bu enum'u projenize ekleyin.
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
        // --- Profil Metotlar� ---

        /// <summary>
        /// Yeni bir kullan�c� i�in veritaban�nda profil ve ba�lang�� istatistiklerini olu�turur.
        /// </summary>
        Task<ServiceResult<bool>> CreateUserProfileAsync(string userId, string username, string email);

        /// <summary>
        /// Belirtilen kullan�c�n�n genel profil bilgilerini (isim, resim vb.) getirir.
        /// </summary>
        Task<ServiceResult<UserProfile>> GetUserProfileAsync(string userId);


        // --- �statistik ve Oyunla�t�rma Metotlar� ---

        /// <summary>
        /// Belirtilen kullan�c�n�n istatistiklerini (puan, takas say�s� vb.) getirir.
        /// </summary>
        Task<ServiceResult<UserStats>> GetUserStatsAsync(string userId);

        /// <summary>
        /// Belirli bir aksiyon i�in kullan�c�ya standart puan ekler.
        /// </summary>
        Task<ServiceResult<bool>> AddPointsForAction(string userId, UserAction action);

        /// <summary>
        /// Belirtilen sebeple kullan�c�ya belirli bir miktar puan ekler.
        /// </summary>
        Task<ServiceResult<bool>> AddPointsAsync(string userId, int points, string reason);

        /// <summary>
        /// Kullan�c�n�n mevcut istatistiklerine g�re yeni rozetler kazan�p kazanmad���n� kontrol eder ve gerekirse verir.
        /// </summary>
        Task<ServiceResult<bool>> CheckAndAwardBadgesAsync(string userId);

        /// <summary>
        /// Kullan�c�n�n sahip oldu�u t�m rozetleri listeler.
        /// </summary>
        Task<ServiceResult<List<UserBadge>>> GetUserBadgesAsync(string userId);

        /// <summary>
        /// Kullan�c�ya belirli bir rozeti manuel olarak verir.
        /// </summary>
        Task<ServiceResult<UserBadge>> AwardBadgeAsync(string userId, string badgeId);

        Task<ServiceResult<bool>> TransferTimeCreditsAsync(string fromUserId, string toUserId, int amount, string reason);

        // Not: UpdateUserStatsAsync metodu, puan/rozet ekleme i�lemleri taraf�ndan dolayl� olarak
        // kullan�ld��� i�in genellikle do�rudan �a�r�lmaz. �htiya� halinde kullan�labilir.
        Task<ServiceResult<bool>> UpdateUserStatsAsync(UserStats stats);
    
    
    
    }
}