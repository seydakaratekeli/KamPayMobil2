using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace KamPay.ViewModels
{
    public partial class ServiceRequestsViewModel : ObservableObject
    {
        private readonly IServiceSharingService _serviceSharingService;
        private readonly IAuthenticationService _authenticationService;

        [ObservableProperty]
        private bool isLoading;

        public ObservableCollection<ServiceRequest> MyServiceRequests { get; } = new();

        public ServiceRequestsViewModel(IServiceSharingService serviceSharingService, IAuthenticationService authenticationService)
        {
            _serviceSharingService = serviceSharingService;
            _authenticationService = authenticationService;
        }

        [RelayCommand]
        private async Task RespondToRequestAsync(object parameter)
        {
            if (parameter == null) return;

            string requestId;
            bool accept;

            if (parameter is Tuple<string, bool> tuple)
            {
                requestId = tuple.Item1;
                accept = tuple.Item2;
            }
            else if (parameter is string id) // Kabul Et butonu doðrudan string gönderir
            {
                requestId = id;
                accept = true;
            }
            else
            {
                return;
            }

            // ... (Metodun geri kalaný ayný)
            try
            {
                var action = accept ? "kabul etmek" : "reddetmek";
                bool confirmation = await Application.Current.MainPage.DisplayAlert(
                    "Onay",
                    $"Bu hizmet talebini {action} istediðinizden emin misiniz?",
                    "Evet", "Hayýr"
                );

                if (!confirmation) return;

                IsLoading = true;
                var result = await _serviceSharingService.RespondToRequestAsync(requestId, accept);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Baþarýlý", "Talep yanýtlandý.", "Tamam");
                    // Sayfayý yenile
                    await LoadMyRequestsAsync();
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message ?? "Ýþlem baþarýsýz.", "Tamam");
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
        private async Task LoadMyRequestsAsync()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;
                MyServiceRequests.Clear();

                var currentUser = await _authenticationService.GetCurrentUserAsync();
                if (currentUser == null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Kullanýcý bulunamadý.", "Tamam");
                    return;
                }

                var result = await _serviceSharingService.GetMyServiceRequestsAsync(currentUser.UserId);

                if (result.Success && result.Data != null)
                {
                    foreach (var request in result.Data)
                    {
                        MyServiceRequests.Add(request);
                    }
                }
                else
                {
                    // Hata mesajý göstermek yerine boþ listeyi göstermek daha iyi bir UX olabilir.
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Talepler yüklenirken bir hata oluþtu: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

   
    }
}