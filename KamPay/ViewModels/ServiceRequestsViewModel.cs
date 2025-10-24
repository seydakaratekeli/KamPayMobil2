using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System;

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

        public ObservableCollection<PaymentOption> PaymentMethods { get; }

        public ServiceRequestsViewModel(IServiceSharingService serviceService, IAuthenticationService authService)
        {
            _serviceService = serviceService;
            _authService = authService;

            // 🟢 Ödeme yöntemleri
            PaymentMethods = new ObservableCollection<PaymentOption>
            {
                new PaymentOption { Method = PaymentMethodType.CardSim, DisplayName = "Kart (Simülasyon)" },
                new PaymentOption { Method = PaymentMethodType.BankTransferSim, DisplayName = "EFT / Havale (Simülasyon)" }
            };
        }

        public class PaymentOption
        {
            public PaymentMethodType Method { get; set; }
            public string DisplayName { get; set; }
        }

        private PaymentMethodType _selectedPaymentMethod = PaymentMethodType.CardSim;
        public PaymentMethodType SelectedPaymentMethod
        {
            get => _selectedPaymentMethod;
            set
            {
                if (_selectedPaymentMethod != value)
                {
                    _selectedPaymentMethod = value;
                    OnPropertyChanged();
                }
            }
        }

        // 🧭 Talepleri yükle
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

                if (result.Success)
                {
                    IncomingRequests.Clear();
                    foreach (var request in result.Data.Incoming)
                        IncomingRequests.Add(request);

                    OutgoingRequests.Clear();
                    foreach (var request in result.Data.Outgoing)
                        OutgoingRequests.Add(request);
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

        // 🟢 Kabul / Reddet işlemleri
        [RelayCommand]
        private async Task AcceptRequestAsync(ServiceRequest request) =>
            await HandleResponseAsync(request, true);

        [RelayCommand]
        private async Task DeclineRequestAsync(ServiceRequest request) =>
            await HandleResponseAsync(request, false);

        private async Task HandleResponseAsync(ServiceRequest request, bool accepted)
        {
            if (request == null || request.Status != ServiceRequestStatus.Pending)
                return;

            var result = await _serviceService.RespondToRequestAsync(request.RequestId, accepted);
            if (result.Success)
                await LoadRequestsAsync();
            else
                await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
        }

        // 💰 Tamamlama ve ödeme simülasyonu
        [RelayCommand]
        private async Task CompleteRequestAsync(ServiceRequest request)
        {
            if (request == null || request.Status != ServiceRequestStatus.Accepted)
                return;

            // 🟡 Hizmet fiyatı kontrolü
            decimal price = request.QuotedPrice ?? 0;
            string priceInfo = price > 0 ? $"Bu hizmetin ücreti {price} ₺ olarak kaydedilmiştir.\n\n" : "";

            var confirm = await Shell.Current.DisplayAlert(
                "Onay",
                $"{priceInfo}Hizmeti aldığınızı onaylıyor musunuz? Bu işlem geri alınamaz ve ödeme simülasyonu başlatılacaktır.",
                "Evet, Onayla",
                "Hayır"
            );

            if (!confirm) return;

            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                await Shell.Current.DisplayAlert("Hata", "Oturum bilgisi alınamadı.", "Tamam");
                return;
            }

            try
            {
                // 🟢 Ödeme simülasyonu başlat
                var result = await _serviceService.SimulatePaymentAndCompleteAsync(
                    request.RequestId,
                    currentUser.UserId,
                    SelectedPaymentMethod
                );

                if (result.Success)
                {
                    string message = SelectedPaymentMethod switch
                    {
                        PaymentMethodType.CardSim => $"Kart (Simülasyon) ile {price} ₺ ödeme başarıyla gerçekleştirildi.",
                        PaymentMethodType.BankTransferSim => $"EFT / Havale (Simülasyon) ile {price} ₺ ödeme başarıyla tamamlandı.",
                        _ => "Ödeme simülasyonu başarıyla tamamlandı."
                    };

                    await Shell.Current.DisplayAlert("Başarılı", message, "Tamam");
                    await LoadRequestsAsync(); // Listeyi yenile
                }
                else
                {
                    await Shell.Current.DisplayAlert("Hata", result.Message, "Tamam");
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Hata", ex.Message, "Tamam");
            }
        }
    }
}
