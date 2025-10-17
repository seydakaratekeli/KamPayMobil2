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

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string errorMessage;

        [ObservableProperty]
        private Product redemptionResult;

        [ObservableProperty]
        private bool canRedeem = true;

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        // Event for the View to subscribe to
        public event EventHandler<bool> RedemptionCompleted;

        public SurpriseBoxViewModel(ISurpriseBoxService surpriseBoxService, IAuthenticationService authenticationService)
        {
            _surpriseBoxService = surpriseBoxService;
            _authenticationService = authenticationService;
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
                RedemptionCompleted?.Invoke(this, false);
            }
            finally
            {
                IsLoading = false;
                // 'CanRedeem'i burada true yapmıyoruz,
                // kullanıcı sonuç ekranını kapattığında ResetCommand ile yapacağız.
            }
        }

        [RelayCommand]
        private void Reset()
        {
            ErrorMessage = string.Empty;
            RedemptionResult = null;
            CanRedeem = true;
        }
    }
}