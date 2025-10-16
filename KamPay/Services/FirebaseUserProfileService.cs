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


    public class FirebaseUserProfileService : IUserProfileService
    {
        private readonly FirebaseClient _firebaseClient;

        public FirebaseUserProfileService()
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
        }

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
                    // Yeni kullanıcı için istatistik oluştur
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

        public async Task<ServiceResult<UserBadge>> AwardBadgeAsync(string userId, string badgeId)
        {
            try
            {
                // Zaten kazanılmış mı kontrol et
                var existingBadges = await GetUserBadgesAsync(userId);
                if (existingBadges.Success && existingBadges.Data.Any(b => b.BadgeId == badgeId))
                {
                    return ServiceResult<UserBadge>.FailureResult("Rozet zaten kazanılmış");
                }

                // Badge bilgilerini al
                var badge = await _firebaseClient
                    .Child(Constants.BadgesCollection)
                    .Child(badgeId)
                    .OnceSingleAsync<Badge>();

                if (badge == null)
                {
                    return ServiceResult<UserBadge>.FailureResult("Rozet bulunamadı");
                }

                // UserBadge oluştur
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

                // Bildirim oluştur
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

                // Puan bildirimi gönder
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

                // Rozet kontrolü yap
                await CheckAndAwardBadgesAsync(userId);

                return ServiceResult<bool>.SuccessResult(true, $"+{points} puan");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Puan eklenemedi", ex.Message);
            }
        }

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

                // Tüm rozetleri kontrol et
                var allBadges = Badge.GetDefaultBadges();

                foreach (var badge in allBadges)
                {
                    // Zaten kazanılmışsa atla
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
                        // Rozeti kaydet
                        await _firebaseClient
                            .Child(Constants.BadgesCollection)
                            .Child(badge.BadgeId)
                            .PutAsync(badge);

                        // Kullanıcıya ver
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