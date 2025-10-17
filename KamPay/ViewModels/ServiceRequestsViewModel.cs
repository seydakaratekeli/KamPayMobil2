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
            else if (parameter is string id) // Kabul Et butonu do�rudan string g�nderir
            {
                requestId = id;
                accept = true;
            }
            else
            {
                return;
            }

            // ... (Metodun geri kalan� ayn�)
            try
            {
                var action = accept ? "kabul etmek" : "reddetmek";
                bool confirmation = await Application.Current.MainPage.DisplayAlert(
                    "Onay",
                    $"Bu hizmet talebini {action} istedi�inizden emin misiniz?",
                    "Evet", "Hay�r"
                );

                if (!confirmation) return;

                IsLoading = true;
                var result = await _serviceSharingService.RespondToRequestAsync(requestId, accept);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Ba�ar�l�", "Talep yan�tland�.", "Tamam");
                    // Sayfay� yenile
                    await LoadMyRequestsAsync();
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message ?? "��lem ba�ar�s�z.", "Tamam");
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
                    await Application.Current.MainPage.DisplayAlert("Hata", "Kullan�c� bulunamad�.", "Tamam");
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
                    // Hata mesaj� g�stermek yerine bo� listeyi g�stermek daha iyi bir UX olabilir.
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Talepler y�klenirken bir hata olu�tu: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

   
    }
}