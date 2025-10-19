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

            return ServiceResult<GoodDeedPost>.SuccessResult(post, "�lan payla��ld�!");
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

            return ServiceResult<bool>.SuccessResult(true, "�lan silindi");
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
            // 1. �lgili ilan� veritaban�ndan �ek
            var postNode = _firebaseClient.Child(GoodDeedPostsCollection).Child(postId);
            var post = await postNode.OnceSingleAsync<GoodDeedPost>();

            if (post == null)
            {
                return ServiceResult<Comment>.FailureResult("Yorum yap�lmak istenen ilan bulunamad�.");
            }

            // 2. Yorumlar s�zl��� (Dictionary) bo�sa olu�tur, de�ilse yeni yorumu ekle
            if (post.Comments == null)
            {
                post.Comments = new Dictionary<string, Comment>();
            }
            post.Comments[comment.CommentId] = comment;

            // 3. �lan�n yorum sayac�n� s�zl�kteki eleman say�s�na g�re g�ncelle
            post.CommentCount = post.Comments.Count;

            // 4. �lan�n tamam�n�, g�ncellenmi� yorum listesiyle birlikte tek bir i�lemde veritaban�na geri yaz
            await postNode.PutAsync(post);

            // TODO: �lan sahibine yeni bir yorum yap�ld���na dair bildirim g�nderilebilir.

            return ServiceResult<Comment>.SuccessResult(comment, "Yorum eklendi.");
        }
        catch (Exception ex)
        {
            return ServiceResult<Comment>.FailureResult("Yorum eklenirken bir hata olu�tu.", ex.Message);
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
            return ServiceResult<List<Comment>>.FailureResult("Yorumlar al�namad�.", ex.Message);
        }
    }
}