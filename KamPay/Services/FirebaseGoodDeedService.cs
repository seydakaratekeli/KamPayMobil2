using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Models;
using KamPay.Helpers;

namespace KamPay.Services;
public class FirebaseGoodDeedService : IGoodDeedService
{
    private readonly FirebaseClient _firebaseClient;
    private const string GoodDeedPostsCollection = "good_deed_posts";

    public FirebaseGoodDeedService()
    {
        _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
    }

    public async Task<ServiceResult<GoodDeedPost>> CreatePostAsync(GoodDeedPost post)
    {
        try
        {
            await _firebaseClient
                .Child(GoodDeedPostsCollection)
                .Child(post.PostId)
                .PutAsync(post);

            return ServiceResult<GoodDeedPost>.SuccessResult(post, "Ýlan paylaþýldý!");
        }
        catch (Exception ex)
        {
            return ServiceResult<GoodDeedPost>.FailureResult("Hata", ex.Message);
        }
    }

    public async Task<ServiceResult<List<GoodDeedPost>>> GetPostsAsync()
    {
        try
        {
            var allPosts = await _firebaseClient
                .Child(GoodDeedPostsCollection)
                .OnceAsync<GoodDeedPost>();

            var posts = allPosts
                .Select(p => p.Object)
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            return ServiceResult<List<GoodDeedPost>>.SuccessResult(posts);
        }
        catch (Exception ex)
        {
            return ServiceResult<List<GoodDeedPost>>.FailureResult("Hata", ex.Message);
        }
    }

    public async Task<ServiceResult<bool>> LikePostAsync(string postId, string userId)
    {
        try
        {
            var post = await _firebaseClient
                .Child(GoodDeedPostsCollection)
                .Child(postId)
                .OnceSingleAsync<GoodDeedPost>();

            if (post != null)
            {
                post.LikeCount++;
                await _firebaseClient
                    .Child(GoodDeedPostsCollection)
                    .Child(postId)
                    .PutAsync(post);
            }

            return ServiceResult<bool>.SuccessResult(true);
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.FailureResult("Hata", ex.Message);
        }
    }

    public async Task<ServiceResult<bool>> DeletePostAsync(string postId, string userId)
    {
        try
        {
            var post = await _firebaseClient
                .Child(GoodDeedPostsCollection)
                .Child(postId)
                .OnceSingleAsync<GoodDeedPost>();

            if (post == null || post.UserId != userId)
            {
                return ServiceResult<bool>.FailureResult("Yetkiniz yok");
            }

            await _firebaseClient
                .Child(GoodDeedPostsCollection)
                .Child(postId)
                .DeleteAsync();

            return ServiceResult<bool>.SuccessResult(true, "Ýlan silindi");
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.FailureResult("Hata", ex.Message);
        }
    }

    public async Task<ServiceResult<Comment>> AddCommentAsync(string postId, Comment comment)
    {
        try
        {
            // createdAt ve commentId güvenliði
            if (string.IsNullOrWhiteSpace(comment.CommentId))
                comment.CommentId = Guid.NewGuid().ToString();

            if (comment.CreatedAt == default)
                comment.CreatedAt = DateTime.UtcNow;

            var commentsNode = _firebaseClient
                .Child(GoodDeedPostsCollection)
                .Child(postId)
                .Child("Comments");

            // 1) Yeni yorumu ayrý bir child olarak ekle (PostAsync kullan)
            var postResult = await commentsNode.PostAsync(comment);
            comment.CommentId = postResult.Key; // Firebase tarafýndan üretilen key'i sakla (güncelleme)

            // 2) Tüm yorumlarý tekrar çek ve gerçek sayýyý hesapla
            var allComments = await commentsNode.OnceAsync<Comment>();
            var commentCount = allComments?.Count ?? 0;

            // 3) CommentCount alanýný güncelle
            await _firebaseClient
                .Child(GoodDeedPostsCollection)
                .Child(postId)
                .Child("CommentCount")
                .PutAsync(commentCount);

            // Opsiyonel: Eðer post objesi tutuluyorsa güncellemek istersin (viewmodel tarafý genelde yapýyor)
            return ServiceResult<Comment>.SuccessResult(comment, "Yorum eklendi.");
        }
        catch (Exception ex)
        {
            return ServiceResult<Comment>.FailureResult("Yorum eklenirken bir hata oluþtu.", ex.Message);
        }
    }

    public async Task<ServiceResult<List<Comment>>> GetCommentsAsync(string postId)
    {
        try
        {
            var post = await _firebaseClient
                .Child(GoodDeedPostsCollection)
                .Child(postId)
                .OnceSingleAsync<GoodDeedPost>();

            var comments = post?.Comments?.Values
                .OrderBy(c => c.CreatedAt)
                .ToList() ?? new List<Comment>();

            return ServiceResult<List<Comment>>.SuccessResult(comments);
        }
        catch (Exception ex)
        {
            return ServiceResult<List<Comment>>.FailureResult("Yorumlar alýnamadý.", ex.Message);
        }
    }
}