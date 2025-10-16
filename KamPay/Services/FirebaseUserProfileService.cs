using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;

namespace KamPay.Services
{
    // YENİ: Puan verilecek eylemleri tanımlayan enum.
    // Sınıfın dışında ama namespace'in içinde tanımlamak iyi bir pratiktir.
    public enum UserAction
    {
        AddProduct,
        MakeDonation,
        ReceiveBadge,
        CompleteTransaction, // Başarılı bir takas/satış tamamlama
        ReceiveDonation      // Bir bağışı teslim alma
    }

    public class FirebaseUserProfileService : IUserProfileService
    {
        private readonly FirebaseClient _firebaseClient;

        public FirebaseUserProfileService()
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
        }

        // YENİ: Belirli bir eylem için puan ekleyen ana metot.
        public async Task<ServiceResult<bool>> AddPointsForAction(string userId, UserAction action)
        {
            int points = GetPointsForAction(action);
            string reason = GetReasonForAction(action);

            if (points > 0)
            {
                return await AddPointsAsync(userId, points, reason);
            }

            return ServiceResult<bool>.SuccessResult(true, "Bu eylem için puan tanımlanmamış.");
        }

        // YENİ: Hangi eylemin kaç puan kazandıracağını belirleyen yardımcı metot.
        private int GetPointsForAction(UserAction action)
        {
            return action switch
            {
                UserAction.AddProduct => 10,
                UserAction.MakeDonation => 50,
                UserAction.CompleteTransaction => 25, // Başarılı takas için 25 puan
                UserAction.ReceiveDonation => 10,   // Bağış teslim alındığı için 10 puan
                _ => 0
            };
        }

        // YENİ: Puan kazanma nedenini bildirimlerde göstermek için metin döndüren yardımcı metot.
        private string GetReasonForAction(UserAction action)
        {
            return action switch
            {
                UserAction.AddProduct => "Yeni ürün ekledin.",
                UserAction.MakeDonation => "Değerli bir bağış yaptın.",
                UserAction.CompleteTransaction => "Başarılı bir takas/satış tamamladın.",
                UserAction.ReceiveDonation => "Bir bağışı teslim aldın.",
                _ => "Genel aktivite."
            };
        }

        // MEVCUT KODUNUZ (DEĞİŞİKLİK YOK)
        public async Task<ServiceResult<UserStats>> GetUserStatsAsync(string userId)
        {
            try
            {
                var stats = await _firebaseClient
                    .Child(Constants.UserStatsCollection)
                    .Child(userId)
                    .OnceSingleAsync<UserStats>();

                if (stats == null)
                {
                    var user = await _firebaseClient
                        .Child(Constants.UsersCollection)
                        .Child(userId)
                        .OnceSingleAsync<User>();

                    stats = new UserStats
                    {
                        UserId = userId,
                        MemberSince = user?.CreatedAt ?? DateTime.UtcNow
                    };

                    await _firebaseClient
                        .Child(Constants.UserStatsCollection)
                        .Child(userId)
                        .PutAsync(stats);
                }

                return ServiceResult<UserStats>.SuccessResult(stats);
            }
            catch (Exception ex)
            {
                return ServiceResult<UserStats>.FailureResult("İstatistikler yüklenemedi", ex.Message);
            }
        }

        // MEVCUT KODUNUZ (DEĞİŞİKLİK YOK)
        public async Task<ServiceResult<bool>> UpdateUserStatsAsync(UserStats stats)
        {
            try
            {
                stats.LastActivityAt = DateTime.UtcNow;

                await _firebaseClient
                    .Child(Constants.UserStatsCollection)
                    .Child(stats.UserId)
                    .PutAsync(stats);

                return ServiceResult<bool>.SuccessResult(true);
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Güncellenemedi", ex.Message);
            }
        }

        // MEVCUT KODUNUZ (DEĞİŞİKLİK YOK)
        public async Task<ServiceResult<List<UserBadge>>> GetUserBadgesAsync(string userId)
        {
            try
            {
                var allBadges = await _firebaseClient
                    .Child(Constants.UserBadgesCollection)
                    .OnceAsync<UserBadge>();

                var userBadges = allBadges
                    .Select(b => b.Object)
                    .Where(b => b.UserId == userId)
                    .OrderByDescending(b => b.EarnedAt)
                    .ToList();

                return ServiceResult<List<UserBadge>>.SuccessResult(userBadges);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<UserBadge>>.FailureResult("Rozetler yüklenemedi", ex.Message);
            }
        }

        // MEVCUT KODUNUZ (DEĞİŞİKLİK YOK)
        public async Task<ServiceResult<UserBadge>> AwardBadgeAsync(string userId, string badgeId)
        {
            try
            {
                var existingBadges = await GetUserBadgesAsync(userId);
                if (existingBadges.Success && existingBadges.Data.Any(b => b.BadgeId == badgeId))
                {
                    return ServiceResult<UserBadge>.FailureResult("Rozet zaten kazanılmış");
                }

                var badge = await _firebaseClient
                    .Child(Constants.BadgesCollection)
                    .Child(badgeId)
                    .OnceSingleAsync<Badge>();

                if (badge == null)
                {
                    return ServiceResult<UserBadge>.FailureResult("Rozet bulunamadı");
                }

                var userBadge = new UserBadge
                {
                    UserId = userId,
                    BadgeId = badgeId,
                    BadgeName = badge.Name,
                    BadgeIcon = badge.IconName,
                    BadgeColor = badge.Color
                };

                await _firebaseClient
                    .Child(Constants.UserBadgesCollection)
                    .Child(userBadge.UserBadgeId)
                    .PutAsync(userBadge);

                var notification = new Notification
                {
                    UserId = userId,
                    Type = NotificationType.BadgeEarned,
                    Title = "🎉 Yeni Rozet Kazandın!",
                    Message = $"\"{badge.Name}\" rozetini kazandın: {badge.Description}",
                    RelatedEntityId = badgeId,
                    RelatedEntityType = "Badge"
                };

                await _firebaseClient
                    .Child(Constants.NotificationsCollection)
                    .Child(notification.NotificationId)
                    .PutAsync(notification);

                return ServiceResult<UserBadge>.SuccessResult(userBadge, $"Tebrikler! {badge.Name} kazandınız!");
            }
            catch (Exception ex)
            {
                return ServiceResult<UserBadge>.FailureResult("Rozet verilemedi", ex.Message);
            }
        }

        // MEVCUT KODUNUZ (DEĞİŞİKLİK YOK)
        public async Task<ServiceResult<bool>> AddPointsAsync(string userId, int points, string reason)
        {
            try
            {
                var statsResult = await GetUserStatsAsync(userId);
                if (!statsResult.Success)
                {
                    return ServiceResult<bool>.FailureResult("İstatistikler alınamadı");
                }

                var stats = statsResult.Data;
                stats.DonationPoints += points;

                await UpdateUserStatsAsync(stats);

                var notification = new Notification
                {
                    UserId = userId,
                    Type = NotificationType.PointsEarned,
                    Title = "⭐ Puan Kazandın!",
                    Message = $"{points} puan kazandın! Toplam: {stats.DonationPoints} puan\nNeden: {reason}",
                    RelatedEntityType = "Points"
                };

                await _firebaseClient
                    .Child(Constants.NotificationsCollection)
                    .Child(notification.NotificationId)
                    .PutAsync(notification);

                await CheckAndAwardBadgesAsync(userId);

                return ServiceResult<bool>.SuccessResult(true, $"+{points} puan");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Puan eklenemedi", ex.Message);
            }
        }

        // MEVCUT KODUNUZ (DEĞİŞİKLİK YOK)
        public async Task<ServiceResult<bool>> CheckAndAwardBadgesAsync(string userId)
        {
            try
            {
                var statsResult = await GetUserStatsAsync(userId);
                if (!statsResult.Success)
                {
                    return ServiceResult<bool>.FailureResult("İstatistikler alınamadı");
                }

                var stats = statsResult.Data;
                var userBadgesResult = await GetUserBadgesAsync(userId);
                var earnedBadgeIds = userBadgesResult.Success
                    ? userBadgesResult.Data.Select(b => b.BadgeId).ToList()
                    : new List<string>();

                var allBadges = Badge.GetDefaultBadges();

                foreach (var badge in allBadges)
                {
                    if (earnedBadgeIds.Contains(badge.BadgeId))
                        continue;

                    bool shouldAward = false;

                    switch (badge.Category)
                    {
                        case BadgeCategory.Seller:
                            shouldAward = stats.TotalProducts >= badge.RequiredCount;
                            break;
                        case BadgeCategory.Buyer:
                            shouldAward = stats.PurchasedProducts >= badge.RequiredCount;
                            break;
                        case BadgeCategory.Donation:
                            shouldAward = stats.DonatedProducts >= badge.RequiredCount;
                            break;
                        case BadgeCategory.Points:
                            shouldAward = stats.DonationPoints >= badge.RequiredPoints;
                            break;
                    }

                    if (shouldAward)
                    {
                        await _firebaseClient
                            .Child(Constants.BadgesCollection)
                            .Child(badge.BadgeId)
                            .PutAsync(badge);

                        await AwardBadgeAsync(userId, badge.BadgeId);
                    }
                }

                return ServiceResult<bool>.SuccessResult(true);
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Kontrol edilemedi", ex.Message);
            }
        }
    }
}