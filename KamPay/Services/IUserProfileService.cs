
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
    public interface IUserProfileService
    {
        Task<ServiceResult<UserStats>> GetUserStatsAsync(string userId);
        Task<ServiceResult<bool>> UpdateUserStatsAsync(UserStats stats);
        Task<ServiceResult<List<UserBadge>>> GetUserBadgesAsync(string userId);
        Task<ServiceResult<UserBadge>> AwardBadgeAsync(string userId, string badgeId);
        Task<ServiceResult<bool>> AddPointsAsync(string userId, int points, string reason);
        Task<ServiceResult<bool>> CheckAndAwardBadgesAsync(string userId);
    }
}