using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using KamPay.Models;
using KamPay.Services;

namespace KamPay.ViewModels;

public partial class GoodDeedBoardViewModel : ObservableObject
{
    private readonly IGoodDeedService _goodDeedService;
    private readonly IAuthenticationService _authService;

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

    public GoodDeedBoardViewModel(IGoodDeedService goodDeedService, IAuthenticationService authService)
    {
        _goodDeedService = goodDeedService;
        _authService = authService;
        LoadPostsAsync();
    }

    private async Task LoadPostsAsync()
    {
        IsLoading = true;

        var postsResult = await _goodDeedService.GetPostsAsync();
        var currentUser = await _authService.GetCurrentUserAsync();

        if (postsResult.Success && currentUser != null)
        {
            // �NCEK� HATALI KOD:
            // Posts = new ObservableCollection<GoodDeedPost>(postsResult.Data.OrderByDescending(p => p.CreatedAt));

            // YEN� VE DO�RU KOD:
            // 1. Mevcut koleksiyonun i�ini temizle
            Posts.Clear();

            // 2. Veritaban�ndan gelen yeni verileri d�ng�yle ekle
            var sortedPosts = postsResult.Data.OrderByDescending(p => p.CreatedAt);
            foreach (var post in sortedPosts)
            {
                post.IsOwner = (post.UserId == currentUser.UserId);
                Posts.Add(post);
            }
        }
        else if (!postsResult.Success)
        {
            // Hata mesaj� g�sterilebilir
            await Shell.Current.DisplayAlert("Hata", "�lanlar y�klenemedi.", "Tamam");
        }
        IsLoading = false;
    }

    [RelayCommand]
    private async Task CreatePostAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Description))
            {
                await Application.Current.MainPage.DisplayAlert("Uyar�", "Ba�l�k ve a��klama gerekli", "Tamam");
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

                await Application.Current.MainPage.DisplayAlert("Ba�ar�l�", "�lan payla��ld�!", "Tamam");
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
                "Bu ilan� silmek istedi�inize emin misiniz?",
                "Evet",
                "Hay�r"
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
}
