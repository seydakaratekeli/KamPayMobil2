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

        // KamPay/ViewModels/ServiceRequestsViewModel.cs

        // KamPay/ViewModels/ServiceRequestsViewModel.cs

        [RelayCommand]
        private async Task LoadRequestsAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null) return;

                // Servisten artýk iki liste içeren bir Tuple geliyor
                var result = await _serviceService.GetMyServiceRequestsAsync(currentUser.UserId);

                // HATA DÜZELTMESÝ: result.Success ile kontrol ediyoruz
                if (result.Success)
                {
                    // Gelen Talepler listesini temizle ve doldur
                    IncomingRequests.Clear();
                    // HATA DÜZELTMESÝ: Tuple içindeki 'Incoming' listesine eriþiyoruz
                    foreach (var request in result.Data.Incoming)
                    {
                        IncomingRequests.Add(request);
                    }

                    // Giden Talepler listesini temizle ve doldur
                    OutgoingRequests.Clear();
                    // HATA DÜZELTMESÝ: Tuple içindeki 'Outgoing' listesine eriþiyoruz
                    foreach (var request in result.Data.Outgoing)
                    {
                        OutgoingRequests.Add(request);
                    }
                }
                else
                {
                    await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
                }
            }
            finally
            {
                IsLoading = false;
            }
        }
        // Kabul Etmek için yeni komut
        [RelayCommand]
        private async Task AcceptRequestAsync(ServiceRequest request)
        {
            await HandleResponseAsync(request, true);
        }

        // Reddetmek için yeni komut
        [RelayCommand]
        private async Task DeclineRequestAsync(ServiceRequest request)
        {
            await HandleResponseAsync(request, false);
        }

        // Ýki komutun da kullanacaðý ortak özel metot
        private async Task HandleResponseAsync(ServiceRequest request, bool accepted)
        {
            if (request == null || request.Status != ServiceRequestStatus.Pending) return;

            var result = await _serviceService.RespondToRequestAsync(request.RequestId, accepted);
            if (result.Success)
            {
                // Listeyi yeniden yükleyerek arayüzü güncelle
                await LoadRequestsAsync();
            }
            else
            {
                await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
            }
        }

        // --- DÜZELTME SONU ---

        [RelayCommand]
        private async Task CompleteRequestAsync(ServiceRequest request)
        {
            if (request == null || request.Status != ServiceRequestStatus.Accepted) return;

            var confirm = await Shell.Current.DisplayAlert("Onay", "Hizmeti aldýðýnýzý onaylýyor musunuz? Bu iþlem geri alýnamaz ve zaman kredisi transfer edilecektir.", "Evet, Onayla", "Hayýr");
            if (!confirm) return;

            var currentUser = await _authService.GetCurrentUserAsync();
            var result = await _serviceService.CompleteRequestAsync(request.RequestId, currentUser.UserId);

            if (result.Success)
            {
                await Shell.Current.DisplayAlert("Baþarýlý", "Hizmet tamamlandý ve kredi transfer edildi.", "Tamam");
                await LoadRequestsAsync(); // Listeyi yenile
            }
            else
            {
                await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
            }
        }
    }
}