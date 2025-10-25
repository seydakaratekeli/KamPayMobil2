using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using System;
using System.Threading.Tasks;

namespace KamPay.ViewModels
{
    public partial class SurpriseBoxViewModel : ObservableObject
    {
        private readonly ISurpriseBoxService _surpriseBoxService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IUserProfileService _userProfileService; // 🔥 YENİ

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string errorMessage;

        [ObservableProperty]
        private Product redemptionResult;

        [ObservableProperty]
        private bool canRedeem = true;

        // 🔥 YENİ: Kullanıcının mevcut puanını göster
        [ObservableProperty]
        private int userPoints;

        [ObservableProperty]
        private string successMessage; // 🔥 YENİ

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        public event EventHandler<bool> RedemptionCompleted;

        public SurpriseBoxViewModel(
            ISurpriseBoxService surpriseBoxService,
            IAuthenticationService authenticationService,
            IUserProfileService userProfileService) // 🔥 YENİ
        {
            _surpriseBoxService = surpriseBoxService;
            _authenticationService = authenticationService;
            _userProfileService = userProfileService; // 🔥 YENİ

            _ = LoadUserPointsAsync(); // 🔥 YENİ
        }

        // 🔥 YENİ: Kullanıcı puanlarını yükle
        private async Task LoadUserPointsAsync()
        {
            try
            {
                var user = await _authenticationService.GetCurrentUserAsync();
                if (user != null)
                {
                    var statsResult = await _userProfileService.GetUserStatsAsync(user.UserId);
                    if (statsResult.Success)
                    {
                        UserPoints = statsResult.Data.Points;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Puan yükleme hatası: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task RedeemBoxAsync()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;
                CanRedeem = false;
                ErrorMessage = string.Empty;
                SuccessMessage = string.Empty; // 🔥 YENİ

                var user = await _authenticationService.GetCurrentUserAsync();
                if (user == null)
                {
                    ErrorMessage = "Lütfen giriş yapın.";
                    RedemptionCompleted?.Invoke(this, false);
                    return;
                }

                var result = await _surpriseBoxService.RedeemSurpriseBoxAsync(user.UserId);

                if (result.Success && result.Data != null)
                {
                    RedemptionResult = result.Data;
                    SuccessMessage = result.Message; // 🔥 YENİ

                    // 🔥 Puanları güncelle
                    await LoadUserPointsAsync();

                    RedemptionCompleted?.Invoke(this, true);
                }
                else
                {
                    ErrorMessage = result.Message;
                    RedemptionCompleted?.Invoke(this, false);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Beklenmedik bir hata oluştu.";
                Console.WriteLine($"❌ RedeemBox hatası: {ex.Message}");
                RedemptionCompleted?.Invoke(this, false);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void Reset()
        {
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty; // 🔥 YENİ
            RedemptionResult = null;
            CanRedeem = true;
        }

        // 🔥 YENİ: Sayfadan çıkınca puanları yenile
        public async Task RefreshAsync()
        {
            await LoadUserPointsAsync();
        }
    }
}