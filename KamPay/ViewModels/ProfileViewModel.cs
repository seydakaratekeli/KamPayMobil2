
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using KamPay.Views;
namespace KamPay.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly IAuthenticationService _authService;
    private readonly IProductService _productService;
    private readonly IUserProfileService _profileService;

    [ObservableProperty]
    private User currentUser;

    [ObservableProperty]
    private UserStats userStats;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isRefreshing;

    public ObservableCollection<Product> MyProducts { get; } = new();
    public ObservableCollection<UserBadge> MyBadges { get; } = new();

    public ProfileViewModel(IAuthenticationService authService, IProductService productService, IUserProfileService profileService)
    {
        _authService = authService;
        _productService = productService;
        _profileService = profileService;

        LoadProfileAsync();
    }

    [RelayCommand]
    private async Task LoadProfileAsync()
    {
        try
        {
            IsLoading = true;

            // Kullanıcı bilgileri
            CurrentUser = await _authService.GetCurrentUserAsync();
            if (CurrentUser == null) return;

            // İstatistikler
            var statsResult = await _profileService.GetUserStatsAsync(CurrentUser.UserId);
            if (statsResult.Success)
            {
                UserStats = statsResult.Data;
            }

            // Ürünler
            var productsResult = await _productService.GetUserProductsAsync(CurrentUser.UserId);
            if (productsResult.Success && productsResult.Data != null)
            {
                MyProducts.Clear();
                foreach (var product in productsResult.Data.Take(10)) // İlk 10 ürün
                {
                    MyProducts.Add(product);
                }
            }

            // Rozetler
            var badgesResult = await _profileService.GetUserBadgesAsync(CurrentUser.UserId);
            if (badgesResult.Success && badgesResult.Data != null)
            {
                MyBadges.Clear();
                foreach (var badge in badgesResult.Data)
                {
                    MyBadges.Add(badge);
                }
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
    private async Task RefreshProfileAsync()
    {
        IsRefreshing = true;
        await LoadProfileAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task EditProfileAsync()
    {
        await Application.Current.MainPage.DisplayAlert(
            "Bilgi",
            "Profil düzenleme özelliği yakında eklenecek",
            "Tamam"
        );
    }

    [RelayCommand]
    private async Task ViewAllProductsAsync()
    {
        // Kendi ürünlerini görüntüle
        await Shell.Current.GoToAsync($"myproducts?userId={CurrentUser.UserId}");
    }

    [RelayCommand]
    private async Task ViewAllBadgesAsync()
    {
        await Application.Current.MainPage.DisplayAlert(
            "🏆 Rozetlerim",
            $"Toplam {MyBadges.Count} rozet kazandınız!\n\n" +
            string.Join("\n", MyBadges.Select(b => $"• {b.BadgeName}")),
            "Tamam"
        );
    }

    [RelayCommand]
    private async Task ShareProfileAsync()
    {
        if (CurrentUser == null) return;

        try
        {
            await Share.RequestAsync(new ShareTextRequest
            {
                Title = "Profilimi Paylaş",
                Text = $"{CurrentUser.FullName}\n" +
               $"🎯 {UserStats?.Points ?? 0} puan\n" + // DÜZELTİLDİ
               $"📦 {UserStats?.TotalProducts ?? 0} ürün\n" +
               $"🏆 {MyBadges.Count} rozet\n\n" +
               "KamPay ile paylaşıldı"
            });
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        var confirm = await Application.Current.MainPage.DisplayAlert(
            "Çıkış",
            "Çıkış yapmak istediğinize emin misiniz?",
            "Evet",
            "Hayır"
        );

        if (!confirm) return;

        try
        {
            await _authService.LogoutAsync();

            // Login sayfasına yönlendir
            await Shell.Current.GoToAsync("//LoginPage");
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
        }
    }

    [RelayCommand]
    private async Task GoToOffersAsync()
    {
        await Shell.Current.GoToAsync(nameof(Views.OffersPage));
    }
    [RelayCommand]
    private async Task ProductTappedAsync(Product product)
    {
        if (product == null) return;
        await Shell.Current.GoToAsync($"productdetail?productId={product.ProductId}");
    }

}
