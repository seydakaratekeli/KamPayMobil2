using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.IO; // Path.GetFileName için eklendi
using Microsoft.Maui.Devices.Sensors; // EKLENDÝ
using Microsoft.Maui.ApplicationModel; // EKLENDÝ

namespace KamPay.ViewModels
{
    public partial class AddProductViewModel : ObservableObject
    {
        private readonly IReverseGeocodeService _reverseGeocodeService;
        private readonly IProductService _productService;
        private readonly IAuthenticationService _authService;
        private readonly IUserProfileService _userProfileService;
        private readonly ICategoryService _categoryService; // YENÝ: Kategoriler için

        [ObservableProperty]
        private double? latitude;

        [ObservableProperty]
        private double? longitude;

        [ObservableProperty]
        private string title;

        [ObservableProperty]
        private string description;

        [ObservableProperty]
        private Category selectedCategory;

        [ObservableProperty]
        private ProductCondition selectedCondition;

        [ObservableProperty]
        private ProductType selectedType;

        [ObservableProperty]
        private decimal price;

        [ObservableProperty]
        private string location;

        [ObservableProperty]
        private string exchangePreference;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string errorMessage;

        [ObservableProperty]
        private bool showPriceField;

        [ObservableProperty]
        private bool showExchangeField;

        [ObservableProperty]
        private bool isForSurpriseBox;

        public bool IsDonationTypeSelected => SelectedType == ProductType.Bagis;


        public ObservableCollection<Category> Categories { get; } = new();
        public ObservableCollection<string> ImagePaths { get; } = new();

        public List<ProductCondition> Conditions { get; } = Enum.GetValues(typeof(ProductCondition))
            .Cast<ProductCondition>()
            .ToList();

        public List<ProductType> ProductTypes { get; } = Enum.GetValues(typeof(ProductType))
            .Cast<ProductType>()
            .ToList();

        // CONSTRUCTOR'I GÜNCELLEYELÝM
        public AddProductViewModel(
           IProductService productService,
            IAuthenticationService authService,
            IUserProfileService userProfileService,
            IStorageService storageService,
            ICategoryService categoryService,
            IReverseGeocodeService reverseGeocodeService) // Parametre eklendi
        {
            _productService = productService;
            _authService = authService;
            _userProfileService = userProfileService;
            _categoryService = categoryService;
            _reverseGeocodeService = reverseGeocodeService; // Atama yapýldý

            // Varsayýlan deðerler
            SelectedCondition = ProductCondition.Iyi;
            SelectedType = ProductType.Satis;
            ShowPriceField = true;
            ShowExchangeField = false;

            // Constructor içinde async çaðýrmak yerine, komutu sayfa açýldýðýnda tetikleyeceðiz.
            // VEYA komutu doðrudan burada çaðýrabiliriz:
            // LoadCategoriesCommand.Execute(null); // Sayfa açýlmadan yüklemeye baþlar
        }

        partial void OnSelectedTypeChanged(ProductType value)
        {
            ShowPriceField = value == ProductType.Satis;
            ShowExchangeField = value == ProductType.Takas;

            // "Baðýþ" seçeneði deðiþtiðinde arayüzün güncellenmesi için haber ver
            OnPropertyChanged(nameof(IsDonationTypeSelected));

            if (value != ProductType.Satis)
            {
                Price = 0;
            }
            // Eðer seçilen tür "Baðýþ" deðilse, Sürpriz Kutu seçeneðini de sýfýrla
            if (value != ProductType.Bagis)
            {
                IsForSurpriseBox = false;
            }
        }

        [RelayCommand]
        private async Task UseCurrentLocationAsync()
        {
            if (IsLoading) return;
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;
                Location = "Konum alýnýyor, lütfen bekleyin...";

                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }

                if (status != PermissionStatus.Granted)
                {
                    Location = string.Empty;
                    await Shell.Current.DisplayAlert("Ýzin Gerekli", "Konum almak için uygulama ayarlarýna giderek izin vermeniz gerekmektedir.", "Tamam");
                    return;
                }

                var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10));
                var deviceLocation = await Geolocation.GetLocationAsync(request);

                if (deviceLocation != null)
                {
                    Latitude = deviceLocation.Latitude;
                    Longitude = deviceLocation.Longitude;

                    // Adres çözümleme iþi artýk servisimize ait
                    Location = await _reverseGeocodeService.GetAddressForLocation(deviceLocation);
                }
                else
                {
                    Location = "Konum bilgisi alýnamadý. GPS'inizin açýk olduðundan emin olun.";
                }
            }
            catch (Exception ex)
            {
                Location = "Konum alýnýrken bir hata oluþtu.";
                System.Diagnostics.Debug.WriteLine($"Konum Hatasý: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task LoadCategoriesAsync()
        {
            if (IsLoading) return;
            try
            {
                IsLoading = true;
                var categoryList = await _categoryService.GetCategoriesAsync(); // Doðru servisten çaðýrýyoruz

                if (categoryList != null)
                {
                    Categories.Clear();
                    foreach (var category in categoryList)
                    {
                        Categories.Add(category);
                    }

                    if (Categories.Any())
                    {
                        SelectedCategory = Categories.First();
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Kategoriler yüklenemedi: {ex.Message}";
                await Shell.Current.DisplayAlert("Hata", ErrorMessage, "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task PickImagesAsync()
        {
            try
            {
                // MAUI Media Picker kullanarak fotoðraf seç
                var photos = await MediaPicker.PickPhotoAsync(new MediaPickerOptions
                {
                    Title = "Ürün Görseli Seçin"
                });

                if (photos != null)
                {
                    // Maksimum görsel sayýsý kontrolü
                    if (ImagePaths.Count >= 5)
                    {
                        await Application.Current.MainPage.DisplayAlert(
                            "Uyarý",
                            "En fazla 5 görsel ekleyebilirsiniz",
                            "Tamam"
                        );
                        return;
                    }

                    // Görseli listeye ekle
                    ImagePaths.Add(photos.FullPath);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Görsel seçilirken hata oluþtu: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RemoveImage(string imagePath)
        {
            if (ImagePaths.Contains(imagePath))
            {
                ImagePaths.Remove(imagePath);
            }
        }


        [RelayCommand]
        private async Task SaveProductAsync()
        {
            if (IsLoading) return;

            // Gerekli alanlarýn dolu olduðunu doðrulayýn
            if (string.IsNullOrWhiteSpace(Title) || SelectedCategory == null || !ImagePaths.Any())
            {
                ErrorMessage = "Lütfen baþlýk, kategori ve en az bir resim eklediðinizden emin olun.";
                await Shell.Current.DisplayAlert("Eksik Bilgi", ErrorMessage, "Tamam");
                return;
            }

            // Konumun alýnmýþ olmasýný kontrol et
            if (Latitude == null || Longitude == null)
            {
                await Shell.Current.DisplayAlert("Eksik Bilgi", "Lütfen ürün konumu almak için konum butonunu kullanýn.", "Tamam");
                return;
            }
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null)
                {
                    await Shell.Current.DisplayAlert("Hata", "Oturum bulunamadý. Lütfen tekrar giriþ yapýn.", "Tamam");
                    return;
                }

                // Modelinize uygun 'ProductRequest' nesnesini oluþturuyoruz.
                // Bu nesne, resimlerin sadece lokal yollarýný içerir.
                var request = new ProductRequest
                {
                    Title = this.Title,
                    Description = this.Description,
                    CategoryId = SelectedCategory.CategoryId,
                    CategoryName = SelectedCategory.Name,
                    Condition = this.SelectedCondition,
                    Type = this.SelectedType,
                    Price = this.Price,
                    Location = this.Location,
                    ExchangePreference = this.ExchangePreference,
                    ImagePaths = this.ImagePaths.ToList(), // Lokal dosya yollarýný servise gönderiyoruz
                    IsForSurpriseBox = this.IsForSurpriseBox
                };

                // Servisi, beklediði doðru parametrelerle çaðýrýyoruz.
                // Resim yükleme iþini bu metot kendi içinde halledecektir.
                var result = await _productService.AddProductAsync(request, currentUser);

                if (result.Success)
                {
                    await _userProfileService.AddPointsForAction(currentUser.UserId, UserAction.AddProduct);
                    await Shell.Current.DisplayAlert("Baþarýlý", "Ürününüz baþarýyla eklendi!", "Harika!");
                    ClearForm();
                    await Shell.Current.GoToAsync("..");
                }
                else
                {
                    ErrorMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ürün kaydedilirken beklenmedik bir hata oluþtu: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task CancelAsync()
        {
            var confirm = await Application.Current.MainPage.DisplayAlert(
                "Ýptal",
                "Ürün eklemeyi iptal etmek istediðinize emin misiniz?",
                "Evet",
                "Hayýr"
            );

            if (confirm)
            {
                ClearForm();
                await Shell.Current.GoToAsync("..");
            }
        }

        private void ClearForm()
        {
            Title = string.Empty;
            Description = string.Empty;
            Price = 0;
            Location = string.Empty;
            ExchangePreference = string.Empty;
            ImagePaths.Clear();
            ErrorMessage = string.Empty;

            if (Categories.Any())
            {
                SelectedCategory = Categories.First();
            }

            SelectedCondition = ProductCondition.Iyi;
            SelectedType = ProductType.Satis;
        }
    }
}