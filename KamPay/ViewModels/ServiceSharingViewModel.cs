using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System;
using KamPay.Models;
using KamPay.Services;

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

                var result = await _serviceService.GetServiceOffersAsync();

                if (result.Success && result.Data != null)
                {
                    Services.Clear();
                    foreach (var service in result.Data)
                    {
                        Services.Add(service);
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
        private async Task CreateServiceAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ServiceTitle) || string.IsNullOrWhiteSpace(ServiceDescription))
                {
                    await Application.Current.MainPage.DisplayAlert("Uyarı", "Başlık ve açıklama gerekli", "Tamam");
                    return;
                }

                IsLoading = true;

                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Giriş yapılmamış", "Tamam");
                    return;
                }

                var service = new ServiceOffer
                {
                    ProviderId = currentUser.UserId,
                    ProviderName = currentUser.FullName,
                    Category = SelectedCategory,
                    Title = ServiceTitle,
                    Description = ServiceDescription,
                    TimeCredits = TimeCredits
                };

                var result = await _serviceService.CreateServiceOfferAsync(service);

                if (result.Success && result.Data != null)
                {
                    Services.Insert(0, result.Data);
                    ServiceTitle = string.Empty;
                    ServiceDescription = string.Empty;

                    await Application.Current.MainPage.DisplayAlert("Başarılı", "Hizmet paylaşıldı!", "Tamam");
                }
                else if (!result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message ?? "Bir hata oluştu", "Tamam");
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
        private async Task RequestServiceAsync(ServiceOffer service)
        {
            try
            {
                if (service == null) return;

                var message = await Application.Current.MainPage.DisplayPromptAsync(
                    "Hizmet Talebi",
                    "Mesajınız:",
                    placeholder: "Merhaba, hizmetinizden yararlanmak istiyorum..."
                );

                if (string.IsNullOrWhiteSpace(message)) return;

                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Giriş yapılmamış", "Tamam");
                    return;
                }

                var result = await _serviceService.RequestServiceAsync(service.ServiceId, currentUser, message);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Başarılı",
                        "Talebiniz gönderildi! Hizmet sağlayıcı sizinle iletişime geçecek.",
                        "Tamam"
                    );
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message ?? "Talep gönderilemedi", "Tamam");
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
        }

        public string GetCategoryName(ServiceCategory category)
        {
            return category switch
            {
                ServiceCategory.Education => "📚 Eğitim",
                ServiceCategory.Technical => "💻 Teknik",
                ServiceCategory.Cooking => "🍳 Yemek",
                ServiceCategory.Childcare => "👶 Çocuk Bakımı",
                ServiceCategory.PetCare => "🐕 Evcil Hayvan",
                ServiceCategory.Translation => "🌐 Çeviri",
                ServiceCategory.Moving => "📦 Taşıma",
                _ => "📌 Diğer"
            };
        }
    }
}
