using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;

namespace KamPay.ViewModels
{
    // ===== SÜRPRİZ KUTU ViewModel =====
    public partial class SurpriseBoxViewModel : ObservableObject
    {
        private readonly ISurpriseBoxService _surpriseBoxService;
        private readonly IAuthenticationService _authService;

        [ObservableProperty]
        private int availableBoxCount;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private SurpriseBox? lastOpenedBox;

        public SurpriseBoxViewModel(ISurpriseBoxService surpriseBoxService, IAuthenticationService authService)
        {
            _surpriseBoxService = surpriseBoxService;
            _authService = authService;
            LoadAvailableCountAsync();
        }

        private async void LoadAvailableCountAsync()
        {
            var result = await _surpriseBoxService.GetAvailableBoxesAsync();
            if (result.Success)
            {
                AvailableBoxCount = result.Data.Count;
            }
        }

        [RelayCommand]
        private async Task OpenSurpriseBoxAsync()
        {
            try
            {
                IsLoading = true;

                var currentUser = await _authService.GetCurrentUserAsync();
                var result = await _surpriseBoxService.OpenRandomBoxAsync(currentUser.UserId);

                if (result.Success)
                {
                    LastOpenedBox = result.Data;
                    AvailableBoxCount--;

                    await Application.Current.MainPage.DisplayAlert(
                        "🎁 Tebrikler!",
                        $"{result.Message}\n\nBağışlayan: {result.Data.DonorName}",
                        "Harika!"
                    );
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Bilgi", result.Message, "Tamam");
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
        private async Task CreateSurpriseBoxAsync(string productId)
        {
            try
            {
                var confirm = await Application.Current.MainPage.DisplayAlert(
                    "Sürpriz Bağış",
                    "Bu ürünü sürpriz kutuya eklemek ister misiniz?\nRastgele bir öğrenci bu hediyeyi alacak.",
                    "Evet",
                    "Hayır"
                );

                if (!confirm) return;

                IsLoading = true;

                var currentUser = await _authService.GetCurrentUserAsync();
                var result = await _surpriseBoxService.CreateSurpriseBoxAsync(productId, currentUser);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Başarılı", result.Message, "Tamam");
                    AvailableBoxCount++;
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
    }
}