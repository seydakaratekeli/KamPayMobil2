using System.Collections.ObjectModel;
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
    private readonly IStorageService _storageService;

    [ObservableProperty]
    private User currentUser;

    [ObservableProperty]
    private UserStats userStats;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isRefreshing;

    // ✅ YENİ EKLEME: Profil fotoğrafı kontrolü için
    [ObservableProperty]
    private bool hasProfileImage;

    public ObservableCollection<Product> MyProducts { get; } = new();
    public ObservableCollection<UserBadge> MyBadges { get; } = new();

    public ProfileViewModel(IAuthenticationService authService,
        IProductService productService,
        IUserProfileService profileService,
        IStorageService storageService)
    {
        _authService = authService;
        _productService = productService;
        _profileService = profileService;
        _storageService = storageService;

        LoadProfileAsync();
    }

    [RelayCommand]
    private async Task LoadProfileAsync()
    {
        try
        {
            IsLoading = true;

            CurrentUser = await _authService.GetCurrentUserAsync();
            if (CurrentUser == null) return;

            // ✅ DÜZELTİLDİ: user_profiles'tan profil bilgilerini çek ve CurrentUser'a senkronize et
            var profileResult = await _profileService.GetUserProfileAsync(CurrentUser.UserId);
            if (profileResult.Success)
            {
                var userProfile = profileResult.Data;

                // User nesnesini UserProfile'dan güncelle
                CurrentUser.FirstName = userProfile.FirstName;
                CurrentUser.LastName = userProfile.LastName;
                CurrentUser.ProfileImageUrl = userProfile.ProfileImageUrl;
                CurrentUser.Email = userProfile.Email;

                // Profil fotoğrafı kontrolü
                HasProfileImage = !string.IsNullOrWhiteSpace(userProfile.ProfileImageUrl);
            }

            // İstatistikleri Yükle
            var statsResult = await _profileService.GetUserStatsAsync(CurrentUser.UserId);
            if (statsResult.Success)
            {
                UserStats = statsResult.Data;
            }
            else
            {
                UserStats = new UserStats();
            }

            // Ürünleri Yükle
            var productsResult = await _productService.GetUserProductsAsync(CurrentUser.UserId);
            if (productsResult.Success && productsResult.Data != null)
            {
                MyProducts.Clear();
                foreach (var product in productsResult.Data.Take(10))
                {
                    MyProducts.Add(product);
                }
                if (UserStats != null)
                {
                    UserStats.TotalProducts = productsResult.Data.Count;
                }
            }

            // Rozetleri Yükle
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
        if (CurrentUser == null)
        {
            await Application.Current.MainPage.DisplayAlert("Hata", "Kullanıcı bilgisi bulunamadı.", "Tamam");
            return;
        }

        // 1. Kullanıcıdan yeni isim bilgilerini alalım
        string newFirstName = await Application.Current.MainPage.DisplayPromptAsync(
            "Profil Güncelle",
            "Yeni adınızı girin:",
            initialValue: CurrentUser.FirstName);

        if (string.IsNullOrWhiteSpace(newFirstName))
            return;

        string newLastName = await Application.Current.MainPage.DisplayPromptAsync(
            "Profil Güncelle",
            "Yeni soyadınızı girin:",
            initialValue: CurrentUser.LastName);

        if (string.IsNullOrWhiteSpace(newLastName))
            return;

        // 2. Kullanıcıdan yeni kullanıcı adını al
        string newUsername = await Application.Current.MainPage.DisplayPromptAsync(
            "Profil Güncelle",
            "Yeni kullanıcı adınızı girin:",
            initialValue: CurrentUser.FirstName + CurrentUser.LastName);

        // 3. İsteğe bağlı: Yeni profil fotoğrafı seçimi
        string uploadedImageUrl = null;
        bool changePhoto = await Application.Current.MainPage.DisplayAlert(
            "Profil Fotoğrafı",
            "Profil fotoğrafını değiştirmek ister misin?",
            "Evet",
            "Hayır");

        if (changePhoto)
        {
            try
            {
                var file = await MediaPicker.PickPhotoAsync(new MediaPickerOptions
                {
                    Title = "Yeni profil fotoğrafı seç"
                });

                if (file != null)
                {
                    var uploadResult = await _storageService.UploadProfileImageAsync(file.FullPath, CurrentUser.UserId);
                    if (uploadResult.Success)
                    {
                        uploadedImageUrl = uploadResult.Data;
                    }
                    else
                    {
                        await Application.Current.MainPage.DisplayAlert("Hata", uploadResult.Message, "Tamam");
                    }
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Fotoğraf yüklenemedi: " + ex.Message, "Tamam");
            }
        }

        IsLoading = true;

        try
        {
            // 4. ✅ DÜZELTİLDİ: user_profiles'ı güncelle
            var result = await _profileService.UpdateUserProfileAsync(
                CurrentUser.UserId,
                firstName: newFirstName,
                lastName: newLastName,
                username: newUsername,
                profileImageUrl: uploadedImageUrl
            );

            if (result.Success)
            {
                // 5. ✅ DÜZELTİLDİ: Local CurrentUser'ı HEMEN güncelle
                CurrentUser.FirstName = newFirstName;
                CurrentUser.LastName = newLastName;

                if (!string.IsNullOrWhiteSpace(uploadedImageUrl))
                {
                    CurrentUser.ProfileImageUrl = uploadedImageUrl;
                    HasProfileImage = true;
                }

                // 6. ✅ KRİTİK: OnPropertyChanged ile UI'ı güncelle
                OnPropertyChanged(nameof(CurrentUser));

                await Application.Current.MainPage.DisplayAlert("Başarılı", "Profil güncellendi!", "Tamam");

                // 7. ✅ Profili yeniden yükle (senkronizasyon için)
                await LoadProfileAsync();
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
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
    private async Task ViewAllProductsAsync()
    {
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
               $"🎯 {UserStats?.Points ?? 0} puan\n" +
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
    private async Task GoToServiceRequests()
    {
        await Shell.Current.GoToAsync(nameof(ServiceRequestsPage));
    }

    [RelayCommand]
    private async Task ProductTappedAsync(Product product)
    {
        if (product == null) return;
        await Shell.Current.GoToAsync($"productdetail?productId={product.ProductId}");
    }
}