using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Firebase.Database;
using KamPay.Helpers; // Constants için
using KamPay.Models;
using KamPay.Services;
using System.Collections.ObjectModel;
using System.Linq; // FirstOrDefault için
using System.Reactive.Linq; // AsObservable için

namespace KamPay.ViewModels
{
    public partial class GoodDeedBoardViewModel : ObservableObject, IDisposable
    {
        private readonly IGoodDeedService _goodDeedService;
        private readonly IAuthenticationService _authService;
        private readonly IUserProfileService _userProfileService; // Profil bilgisi için ekledik
        private IDisposable _postsSubscription; // Gerçek zamanlý dinleyici

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

            // Gerçek zamanlý dinleyiciyi baþlat
            // Not: Bu metot artýk `async void` DEÐÝL.
            // Sayfa açýldýðýnda OnAppearing ile tetiklenmesi daha doðru olur,
            // ama þimdilik bu þekilde býrakabiliriz. 
            // Daha önceki desenimize uymak için bunu da komut yapabiliriz.
            StartListeningForPosts();
        }
        // Adým 1: async void'den kurtul. Bu metot artýk senkron.
        private void StartListeningForPosts()
        {
            IsLoading = true;
            var firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);

            _postsSubscription = firebaseClient
                .Child("good_deed_posts")
                .AsObservable<GoodDeedPost>()
                .Subscribe(async e =>
                {
                    var currentUser = await _authService.GetCurrentUserAsync(); // Her olayda kullanýcýyý kontrol et

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
                                Posts[index] = post;
                            }
                            else
                            {
                                // Yeni eklenenleri tarihe göre sýralý eklemek daha iyi bir UX saðlar
                                var sortedPosts = Posts.Append(post).OrderByDescending(p => p.CreatedAt).ToList();
                                Posts.Clear();
                                foreach (var sortedPost in sortedPosts)
                                {
                                    Posts.Add(sortedPost);
                                }
                            }
                        }
                        else if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.Delete)
                        {
                            if (existingPost != null)
                            {
                                Posts.Remove(existingPost);
                            }
                        }
                    });
                });
            IsLoading = false;
        }

        [RelayCommand]
        private async Task CreatePostAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Description))
                {
                    await Application.Current.MainPage.DisplayAlert("Uyarý", "Baþlýk ve açýklama gerekli", "Tamam");
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

                    await Application.Current.MainPage.DisplayAlert("Baþarýlý", "Ýlan paylaþýldý!", "Tamam");
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
                    "Bu ilaný silmek istediðinize emin misiniz?",
                    "Evet",
                    "Hayýr"
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
                UserProfileImageUrl = userProfile?.Data?.ProfileImageUrl ?? "person_icon.svg", // Varsayýlan bir ikon
                Text = NewCommentText.Trim()
            };

            var result = await _goodDeedService.AddCommentAsync(post.PostId, comment);

            if (result.Success)
            {
                NewCommentText = string.Empty; // Yorum kutusunu temizle
            }
            else
            {
                await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
            }
        }

        // IDisposable implementasyonu
        public void Dispose()
        {
            _postsSubscription?.Dispose();
            _postsSubscription = null;
        }
    }
}