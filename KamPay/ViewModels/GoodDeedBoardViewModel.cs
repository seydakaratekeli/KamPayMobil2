using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;
using KamPay.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using Firebase.Database.Streaming;

namespace KamPay.ViewModels
{
    public partial class GoodDeedBoardViewModel : ObservableObject, IDisposable
    {
        private readonly IGoodDeedService _goodDeedService;
        private readonly IAuthenticationService _authService;
        private readonly IUserProfileService _userProfileService;
        private readonly FirebaseClient _firebaseClient;

        private IDisposable _postsSubscription;
        private readonly Dictionary<string, IDisposable> _commentSubscriptions = new();
        private readonly SemaphoreSlim _commentLock = new(1, 1);

        // 🔥 CACHE: Post güncelleme tracker
        private readonly Dictionary<string, GoodDeedPost> _postsCache = new();
        private bool _initialLoadComplete = false;

        [ObservableProperty]
        private string newCommentText;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string title;

        [ObservableProperty]
        private string description;

        [ObservableProperty]
        private PostType selectedType;

        public ObservableCollection<GoodDeedPost> Posts { get; } = new();
        public List<PostType> PostTypes { get; } = Enum.GetValues(typeof(PostType)).Cast<PostType>().ToList();

        public GoodDeedBoardViewModel(
            IGoodDeedService goodDeedService,
            IAuthenticationService authService,
            IUserProfileService userProfileService)
        {
            _goodDeedService = goodDeedService;
            _authService = authService;
            _userProfileService = userProfileService;
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
        }

        [RelayCommand]
        private async Task CreatePostAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Description))
                {
                    await Application.Current.MainPage.DisplayAlert("Uyarı", "Başlık ve açıklama gerekli", "Tamam");
                    return;
                }

                IsLoading = true;

                var currentUser = await _authService.GetCurrentUserAsync();

                var post = new GoodDeedPost
                {
                    UserId = currentUser.UserId,
                    UserName = currentUser.FullName,
                    Type = SelectedType,
                    Title = Title,
                    Description = Description
                };

                var result = await _goodDeedService.CreatePostAsync(post);

                if (result.Success)
                {
                    // Real-time listener otomatik ekleyecek
                    Title = string.Empty;
                    Description = string.Empty;

                    await Application.Current.MainPage.DisplayAlert("Başarılı", "İlan paylaşıldı!", "Tamam");
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task LikePostAsync(GoodDeedPost post)
        {
            try
            {
                var currentUser = await _authService.GetCurrentUserAsync();
                await _goodDeedService.LikePostAsync(post.PostId, currentUser.UserId);
                // Real-time listener otomatik güncelleyecek
            }
            catch { }
        }

        [RelayCommand]
        private async Task DeletePostAsync(GoodDeedPost post)
        {
            try
            {
                var confirm = await Application.Current.MainPage.DisplayAlert(
                    "Sil",
                    "Bu ilanı silmek istediğinize emin misiniz?",
                    "Evet",
                    "Hayır"
                );

                if (!confirm) return;

                var currentUser = await _authService.GetCurrentUserAsync();
                var result = await _goodDeedService.DeletePostAsync(post.PostId, currentUser.UserId);

                if (result.Success)
                {
                    // Comment listener'ını temizle
                    if (_commentSubscriptions.ContainsKey(post.PostId))
                    {
                        _commentSubscriptions[post.PostId].Dispose();
                        _commentSubscriptions.Remove(post.PostId);
                    }
                    // Real-time listener otomatik kaldıracak
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
        }

        // 🔥 OPTİMİZE: Batch processing ile post listener
        public void StartListeningForPosts()
        {
            if (_postsSubscription != null) return;

            IsLoading = !Posts.Any();
            Console.WriteLine("🔥 GoodDeed listener başlatılıyor...");

            _postsSubscription = _firebaseClient
                .Child("good_deed_posts")
                .AsObservable<GoodDeedPost>()
                .Where(e => e.Object != null)
                .Buffer(TimeSpan.FromMilliseconds(400)) // 🔥 400ms batch
                .Where(batch => batch.Any())
                .Subscribe(async events =>
                {
                    var currentUser = await _authService.GetCurrentUserAsync();

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        try
                        {
                            ProcessPostBatch(events, currentUser);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"❌ Post batch hatası: {ex.Message}");
                        }
                        finally
                        {
                            if (!_initialLoadComplete)
                            {
                                _initialLoadComplete = true;
                                IsLoading = false;
                                Console.WriteLine("✅ İlk post yüklemesi tamamlandı");
                            }
                        }
                    });
                }, ex =>
                {
                    Debug.WriteLine($"❌ İyilik Panosu listener hatası: {ex.Message}");
                    MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
                });
        }

        // 🔥 YENİ: Batch processing - Clear() YOK
        private void ProcessPostBatch(IList<FirebaseEvent<GoodDeedPost>> events, User currentUser)
        {
            bool hasChanges = false;

            foreach (var e in events)
            {
                var post = e.Object;
                post.PostId = e.Key;

                if (currentUser != null)
                {
                    post.IsOwner = post.UserId == currentUser.UserId;
                }

                var existingPost = Posts.FirstOrDefault(p => p.PostId == post.PostId);

                switch (e.EventType)
                {
                    case FirebaseEventType.InsertOrUpdate:
                        if (existingPost != null)
                        {
                            // Güncelleme - pozisyonu koru
                            var index = Posts.IndexOf(existingPost);

                            // 🔥 Comment'leri koru
                            if (existingPost.Comments != null && post.Comments == null)
                            {
                                post.Comments = existingPost.Comments;
                                post.CommentCount = existingPost.CommentCount;
                            }

                            Posts[index] = post;
                            _postsCache[post.PostId] = post;
                        }
                        else
                        {
                            // Yeni post - sıralı ekle
                            InsertPostSorted(post);
                            _postsCache[post.PostId] = post;

                            // Comment listener başlat
                            StartListeningForComments(post);
                        }
                        hasChanges = true;
                        break;

                    case FirebaseEventType.Delete:
                        if (existingPost != null)
                        {
                            Posts.Remove(existingPost);
                            _postsCache.Remove(post.PostId);

                            // Comment listener'ı temizle
                            if (_commentSubscriptions.ContainsKey(post.PostId))
                            {
                                _commentSubscriptions[post.PostId].Dispose();
                                _commentSubscriptions.Remove(post.PostId);
                            }
                            hasChanges = true;
                        }
                        break;
                }
            }

            // 🔥 Sadece değişiklik varsa sırala
            if (hasChanges)
            {
                SortPostsInPlace();
            }
        }

        // 🔥 YENİ: Sıralı insert (en yeni üstte)
        private void InsertPostSorted(GoodDeedPost post)
        {
            if (Posts.Count == 0)
            {
                Posts.Add(post);
                return;
            }

            if (Posts[0].CreatedAt <= post.CreatedAt)
            {
                Posts.Insert(0, post);
                return;
            }

            for (int i = 0; i < Posts.Count; i++)
            {
                if (Posts[i].CreatedAt < post.CreatedAt)
                {
                    Posts.Insert(i, post);
                    return;
                }
            }

            Posts.Add(post);
        }

        // 🔥 YENİ: In-place sorting
        private void SortPostsInPlace()
        {
            var sorted = Posts.OrderByDescending(p => p.CreatedAt).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var currentIndex = Posts.IndexOf(sorted[i]);
                if (currentIndex != i)
                {
                    Posts.Move(currentIndex, i);
                }
            }
        }

        [RelayCommand]
        private async Task AddCommentAsync(GoodDeedPost post)
        {
            if (post == null || string.IsNullOrWhiteSpace(NewCommentText)) return;

            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null) return;

            var userProfile = await _userProfileService.GetUserProfileAsync(currentUser.UserId);

            var comment = new Comment
            {
                PostId = post.PostId,
                UserId = currentUser.UserId,
                UserName = userProfile?.Data?.Username ?? currentUser.FullName,
                UserProfileImageUrl = userProfile?.Data?.ProfileImageUrl ?? "person_icon.svg",
                Text = NewCommentText.Trim(),
                CommentId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow
            };

            var result = await _goodDeedService.AddCommentAsync(post.PostId, comment);

            if (result.Success)
            {
                NewCommentText = string.Empty;
                // Real-time comment listener otomatik güncelleyecek
            }
            else
            {
                await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
            }
        }

        // 🔥 OPTİMİZE: Comment listener - batch processing
        public void StartListeningForComments(GoodDeedPost post)
        {
            if (_commentSubscriptions.ContainsKey(post.PostId))
                return;

            var subscription = _firebaseClient
                .Child("good_deed_posts")
                .Child(post.PostId)
                .Child("Comments")
                .AsObservable<Comment>()
                .Where(e => e.Object != null && !string.IsNullOrEmpty(e.Key))
                .Buffer(TimeSpan.FromMilliseconds(300)) // 🔥 300ms batch
                .Where(batch => batch.Any())
                .Subscribe(async events =>
                {
                    await _commentLock.WaitAsync();
                    try
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            ProcessCommentBatch(post, events);
                        });
                    }
                    finally
                    {
                        _commentLock.Release();
                    }
                });

            _commentSubscriptions[post.PostId] = subscription;
        }

        // 🔥 YENİ: Comment batch processing
        private void ProcessCommentBatch(GoodDeedPost post, IList<FirebaseEvent<Comment>> events)
        {
            post.Comments ??= new Dictionary<string, Comment>();
            bool hasChanges = false;

            foreach (var e in events)
            {
                switch (e.EventType)
                {
                    case FirebaseEventType.InsertOrUpdate:
                        post.Comments[e.Key] = e.Object;
                        hasChanges = true;
                        break;

                    case FirebaseEventType.Delete:
                        if (post.Comments.ContainsKey(e.Key))
                        {
                            post.Comments.Remove(e.Key);
                            hasChanges = true;
                        }
                        break;
                }
            }

            if (hasChanges)
            {
                post.CommentCount = post.Comments.Count;

                // 🔥 UI binding'ini yenile (ObservableCollection pattern)
                var existingPost = Posts.FirstOrDefault(p => p.PostId == post.PostId);
                if (existingPost != null)
                {
                    var index = Posts.IndexOf(existingPost);
                    Posts[index] = post;
                }
            }
        }

        public void StopListening()
        {
            _postsSubscription?.Dispose();
            _postsSubscription = null;

            foreach (var sub in _commentSubscriptions.Values)
                sub.Dispose();

            _commentSubscriptions.Clear();
            _postsCache.Clear();
            _initialLoadComplete = false;

            Console.WriteLine("🧹 GoodDeed listeners temizlendi");
        }

        public void Dispose()
        {
            StopListening();
        }
    }
}