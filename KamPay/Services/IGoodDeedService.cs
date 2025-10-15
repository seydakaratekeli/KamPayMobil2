using KamPay.Models;

namespace KamPay.Services;
public interface IGoodDeedService
{
    Task<ServiceResult<GoodDeedPost>> CreatePostAsync(GoodDeedPost post);
    Task<ServiceResult<List<GoodDeedPost>>> GetPostsAsync();
    Task<ServiceResult<bool>> LikePostAsync(string postId, string userId);
    Task<ServiceResult<bool>> DeletePostAsync(string postId, string userId);
}