using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System;
using KamPay.Models;
using KamPay.Services;
using System.Collections.Generic;

namespace KamPay.ViewModels
{
    public partial class ServiceSharingViewModel : ObservableObject
    {
        private readonly IServiceSharingService _serviceService;
        private readonly IAuthenticationService _authService;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string serviceTitle;

        [ObservableProperty]
        private string serviceDescription;

        [ObservableProperty]
        private ServiceCategory selectedCategory;

        [ObservableProperty]
        private int timeCredits = 1;


        // 🟢 YENİ EKLENDİ: Hizmet fiyatı (₺)
        [ObservableProperty]
        private decimal servicePrice;

        public ObservableCollection<ServiceOffer> Services { get; } = new();

        public List<ServiceCategory> Categories { get; } = Enum.GetValues(typeof(ServiceCategory)).Cast<ServiceCategory>().ToList();

        public ServiceSharingViewModel(IServiceSharingService serviceService, IAuthenticationService authService)
        {
            _serviceService = serviceService ?? throw new ArgumentNullException(nameof(serviceService));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));

            _ = LoadServicesAsync();
        }

        [RelayCommand]
        private async Task LoadServicesAsync()
        {
            try
            {
                IsLoading = true;
                Services.Clear();
                var result = await _serviceService.GetServiceOffersAsync();

                if (result.Success && result.Data != null)
                {
                    foreach (var service in result.Data)
                        Services.Add(service);
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
        private async Task CreateServiceAsync()
        {
            try
            {
                // 🟡 Giriş doğrulama
                if (string.IsNullOrWhiteSpace(ServiceTitle) || string.IsNullOrWhiteSpace(ServiceDescription))
                {
                    await Application.Current.MainPage.DisplayAlert("Uyarı", "Başlık ve açıklama gerekli.", "Tamam");
                    return;
                }

                if (ServicePrice <= 0)
                {
                    await Application.Current.MainPage.DisplayAlert("Uyarı", "Lütfen geçerli bir fiyat giriniz.", "Tamam");
                    return;
                }

                IsLoading = true;

                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Giriş yapılmamış.", "Tamam");
                    return;
                }

                // 🟢 Yeni hizmet oluşturuluyor (fiyat dahil)
                var service = new ServiceOffer
                {
                    ProviderId = currentUser.UserId,
                    ProviderName = currentUser.FullName,
                    Category = SelectedCategory,
                    Title = ServiceTitle,
                    Description = ServiceDescription,
                    TimeCredits = TimeCredits,
                    Price = ServicePrice,
                    CreatedAt = DateTime.UtcNow
                };

                var result = await _serviceService.CreateServiceOfferAsync(service);

                if (result.Success && result.Data != null)
                {
                    Services.Insert(0, result.Data);

                    // Formu sıfırla
                    ServiceTitle = string.Empty;
                    ServiceDescription = string.Empty;
                    ServicePrice = 0;
                    TimeCredits = 1;

                    await Application.Current.MainPage.DisplayAlert("Başarılı", "Hizmet paylaşıldı!", "Tamam");
                }
                else if (!result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message ?? "Bir hata oluştu.", "Tamam");
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

        // --- HİZMET TALEP KOMUTU ---
        [RelayCommand]
        private async Task RequestServiceAsync(ServiceOffer offer)
        {
            if (offer == null) return;

            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Bu işlem için giriş yapmalısınız.", "Tamam");
                return;
            }

            // 🟡 Kullanıcının kendi hizmetini talep etmesini engelle
            if (offer.ProviderId == currentUser.UserId)
            {
                await Application.Current.MainPage.DisplayAlert("Bilgi", "Kendi hizmetinizi talep edemezsiniz.", "Tamam");
                return;
            }

            try
            {
                var message = await Application.Current.MainPage.DisplayPromptAsync(
                    "Hizmet Talebi",
                    $"'{offer.Title}' hizmeti için talebinizi iletin (Fiyat: {offer.Price} ₺):",
                    "Gönder",
                    "İptal",
                    "Merhaba, bu hizmetinizden yararlanmak istiyorum."
                );

                if (string.IsNullOrWhiteSpace(message)) return;

                IsLoading = true;

                var result = await _serviceService.RequestServiceAsync(offer, currentUser, message);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Başarılı", result.Message, "Tamam");
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message ?? "Talep gönderilemedi.", "Tamam");
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
