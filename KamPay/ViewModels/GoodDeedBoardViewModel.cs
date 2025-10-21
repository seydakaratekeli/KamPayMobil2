using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers; // Constants için
using KamPay.Models;
using KamPay.Services;
using System.Collections.ObjectModel;
using System.Diagnostics; 
using System.Linq; // FirstOrDefault için
using System.Reactive.Linq; // AsObservable için

namespace KamPay.ViewModels
{
    public partial class GoodDeedBoardViewModel : ObservableObject, IDisposable
    {
        private readonly IGoodDeedService _goodDeedService;
        private readonly IAuthenticationService _authService;
        private readonly IUserProfileService _userProfileService; // Profil bilgisi için ekledik
        private readonly FirebaseClient _firebaseClient;
        private IDisposable _postsSubscription; // Gerçek zamanlı dinleyici

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

        public GoodDeedBoardViewModel(IGoodDeedService goodDeedService, IAuthenticationService authService, IUserProfileService userProfileService)
        {
            _goodDeedService = goodDeedService;
            _authService = authService;
            _userProfileService = userProfileService; // Servisi ata
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            // Gerçek zamanlı dinleyiciyi başlat
            // Not: Bu metot artık `async void` DEĞİL.
            // Sayfa açıldığında OnAppearing ile tetiklenmesi daha doğru olur,
            // ama şimdilik bu şekilde bırakabiliriz. 
            // Daha önceki desenimize uymak için bunu da komut yapabiliriz.
            // StartListeningForPosts();
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
                    Posts.Insert(0, result.Data);
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
                post.LikeCount++;
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
                    Posts.Remove(post);
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
        }

        public void StartListeningForPosts()
        {
            if (_postsSubscription != null) return;

            IsLoading = !Posts.Any();

            _postsSubscription = _firebaseClient
                .Child("good_deed_posts")
                .AsObservable<GoodDeedPost>()
                .Subscribe(async e =>
                {
                    var currentUser = await _authService.GetCurrentUserAsync();
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        var post = e.Object;
                        post.PostId = e.Key;
                        if (currentUser != null)
                        {
                            post.IsOwner = post.UserId == currentUser.UserId;
                        }

                        var existingPost = Posts.FirstOrDefault(p => p.PostId == post.PostId);

                        if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate)
                        {
                            if (existingPost != null)
                            {
                                var index = Posts.IndexOf(existingPost);
                                // --- DÜZELTME BAŞLANGICI ---
                                // Arayüzün güncellemeyi fark etmesi için eski ilanı kaldırıp
                                // güncel halini aynı pozisyona ekliyoruz.
                                Posts.RemoveAt(index);
                                Posts.Insert(index, post);

                            }
                            else
                            {
                                Posts.Insert(0, post); // Yeni ilanı başa ekle
                            }
                            StartListeningForComments(post);


                        }
                        else if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.Delete)
                        {
                            if (existingPost != null) Posts.Remove(existingPost);
                        }

                        // Sıralama her ihtimale karşı korunabilir
                        var sortedPosts = Posts.OrderByDescending(p => p.CreatedAt).ToList();
                        Posts.Clear();
                        foreach (var p in sortedPosts) Posts.Add(p);

                        IsLoading = false;
                    });
                }, ex =>
                {
                    Debug.WriteLine($"[HATA] İyilik Panosu dinlenirken sorun oluştu: {ex.Message}");
                    MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
                });
        }

        public void StopListening()
        {
            _postsSubscription?.Dispose();
            _postsSubscription = null;

            foreach (var sub in _commentSubscriptions.Values)
                sub.Dispose();

            _commentSubscriptions.Clear();

        }

        public void Dispose()
        {
            StopListening();
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

                // 🌟 UI’yi anında güncelle
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    post.Comments ??= new Dictionary<string, Comment>();
                    post.Comments[comment.CommentId] = comment;
                    post.CommentCount = post.Comments.Count;

                    // ObservableCollection içindeki post'u yenile
                    var existing = Posts.FirstOrDefault(p => p.PostId == post.PostId);
                    if (existing != null)
                    {
                        var index = Posts.IndexOf(existing);
                        Posts.RemoveAt(index);
                        Posts.Insert(index, post);
                    }
                });
            }
            else
            {
                await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
            }
        }

        private readonly Dictionary<string, IDisposable> _commentSubscriptions = new();

        private readonly SemaphoreSlim _commentLock = new(1, 1);

        public void StartListeningForComments(GoodDeedPost post)
        {
            if (_commentSubscriptions.ContainsKey(post.PostId))
                return;

            var subscription = _firebaseClient
                .Child("good_deed_posts")
                .Child(post.PostId)
                .Child("Comments")
                .AsObservable<Comment>()
                .Subscribe(async e =>
                {
                    await _commentLock.WaitAsync();
                    try
                    {
                        if (e.Object == null || string.IsNullOrEmpty(e.Key))
                            return;

                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            post.Comments ??= new Dictionary<string, Comment>();

                            if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate)
                            {
                                post.Comments[e.Key] = e.Object;
                            }
                            else if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.Delete)
                            {
                                if (post.Comments.ContainsKey(e.Key))
                                    post.Comments.Remove(e.Key);
                            }

                            post.CommentCount = post.Comments.Count;

                            // 🧩 Arayüz binding’ini yenilemek için post’u ObservableCollection’a tekrar ekle
                            var existing = Posts.FirstOrDefault(p => p.PostId == post.PostId);
                            if (existing != null)
                            {
                                var index = Posts.IndexOf(existing);
                                Posts.RemoveAt(index);
                                Posts.Insert(index, post);
                            }
                        });
                    }
                    finally
                    {
                        _commentLock.Release();
                    }
                });

            _commentSubscriptions[post.PostId] = subscription;
        }


    }
}