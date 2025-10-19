using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace KamPay.ViewModels
{
    public partial class ServiceRequestsViewModel : ObservableObject
    {
        private readonly IServiceSharingService _serviceService;
        private readonly IAuthenticationService _authService;

        [ObservableProperty]
        private bool isLoading;

        public ObservableCollection<ServiceRequest> IncomingRequests { get; } = new();
        public ObservableCollection<ServiceRequest> OutgoingRequests { get; } = new();

        public ServiceRequestsViewModel(IServiceSharingService serviceService, IAuthenticationService authService)
        {
            _serviceService = serviceService;
            _authService = authService;
        }

        [RelayCommand]
        private async Task LoadRequestsAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null) return;

                var result = await _serviceService.GetMyServiceRequestsAsync(currentUser.UserId);

                if (result.Success && result.Data != null)
                {
                    IncomingRequests.Clear();
                    OutgoingRequests.Clear();
                    var allRequests = result.Data.OrderByDescending(r => r.RequestedAt);
                    foreach (var request in allRequests)
                    {
                        if (request.ProviderId == currentUser.UserId) IncomingRequests.Add(request);
                        else if (request.RequesterId == currentUser.UserId) OutgoingRequests.Add(request);
                    }
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        // --- D�ZELTME BA�LANGICI ---

        // Kabul Etmek i�in yeni komut
        [RelayCommand]
        private async Task AcceptRequestAsync(ServiceRequest request)
        {
            await HandleResponseAsync(request, true);
        }

        // Reddetmek i�in yeni komut
        [RelayCommand]
        private async Task DeclineRequestAsync(ServiceRequest request)
        {
            await HandleResponseAsync(request, false);
        }

        // �ki komutun da kullanaca�� ortak �zel metot
        private async Task HandleResponseAsync(ServiceRequest request, bool accepted)
        {
            if (request == null || request.Status != ServiceRequestStatus.Pending) return;

            var result = await _serviceService.RespondToRequestAsync(request.RequestId, accepted);
            if (result.Success)
            {
                // Listeyi yeniden y�kleyerek aray�z� g�ncelle
                await LoadRequestsAsync();
            }
            else
            {
                await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
            }
        }

        // --- D�ZELTME SONU ---

        [RelayCommand]
        private async Task CompleteRequestAsync(ServiceRequest request)
        {
            if (request == null || request.Status != ServiceRequestStatus.Accepted) return;

            var confirm = await Shell.Current.DisplayAlert("Onay", "Hizmeti ald���n�z� onayl�yor musunuz? Bu i�lem geri al�namaz ve zaman kredisi transfer edilecektir.", "Evet, Onayla", "Hay�r");
            if (!confirm) return;

            var currentUser = await _authService.GetCurrentUserAsync();
            var result = await _serviceService.CompleteRequestAsync(request.RequestId, currentUser.UserId);

            if (result.Success)
            {
                await Shell.Current.DisplayAlert("Ba�ar�l�", "Hizmet tamamland� ve kredi transfer edildi.", "Tamam");
                await LoadRequestsAsync(); // Listeyi yenile
            }
            else
            {
                await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
            }
        }
    }
}