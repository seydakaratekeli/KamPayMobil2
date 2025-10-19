using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.IO; // Path.GetFileName için eklendi

namespace KamPay.ViewModels
{
    public partial class AddProductViewModel : ObservableObject
    {
        private readonly IProductService _productService;
        private readonly IAuthenticationService _authService;
        private readonly IUserProfileService _userProfileService;
        private readonly ICategoryService _categoryService; // YENÝ: Kategoriler için

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
            IUserProfileService userProfileService, // HATA DÜZELTMESÝ: Geri eklendi
            IStorageService storageService, // Eklendi
            ICategoryService categoryService) // Eklendi (IUserProfileService yerine þimdilik bu daha kritik)
        {
            _productService = productService;
            _authService = authService;
            _userProfileService = userProfileService; // HATA DÜZELTMESÝ: Atama yapýldý
            _categoryService = categoryService;

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
        private async Task UseCurrentLocationAsync()
        {
            try
            {
                var location = await Geolocation.GetLastKnownLocationAsync();

                if (location == null)
                {
                    location = await Geolocation.GetLocationAsync(new GeolocationRequest
                    {
                        DesiredAccuracy = GeolocationAccuracy.Medium,
                        Timeout = TimeSpan.FromSeconds(10)
                    });
                }

                if (location != null)
                {
                    // Konum bilgisini string'e çevir
                    // Gerçek uygulamada Geocoding kullanarak adres alýnabilir
                    Location = $"Kampüs ({location.Latitude:F4}, {location.Longitude:F4})";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Konum alýnamadý: {ex.Message}";
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