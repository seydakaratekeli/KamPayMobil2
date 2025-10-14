using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using System.Collections.ObjectModel;

namespace KamPay.ViewModels
{
    public partial class AddProductViewModel : ObservableObject
    {
        private readonly IProductService _productService;
        private readonly IAuthenticationService _authService;

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

        public ObservableCollection<Category> Categories { get; } = new();
        public ObservableCollection<string> ImagePaths { get; } = new();

        public List<ProductCondition> Conditions { get; } = Enum.GetValues(typeof(ProductCondition))
            .Cast<ProductCondition>()
            .ToList();

        public List<ProductType> ProductTypes { get; } = Enum.GetValues(typeof(ProductType))
            .Cast<ProductType>()
            .ToList();

        public AddProductViewModel(IProductService productService, IAuthenticationService authService)
        {
            _productService = productService;
            _authService = authService;

            // Varsayýlan deðerler
            SelectedCondition = ProductCondition.Iyi;
            SelectedType = ProductType.Satis;
            ShowPriceField = true;
            ShowExchangeField = false;

            LoadCategoriesAsync();
        }

        partial void OnSelectedTypeChanged(ProductType value)
        {
            ShowPriceField = value == ProductType.Satis;
            ShowExchangeField = value == ProductType.Takas;

            if (value != ProductType.Satis)
            {
                Price = 0;
            }
        }

        private async void LoadCategoriesAsync()
        {
            var result = await _productService.GetCategoriesAsync();
            if (result.Success && result.Data != null)
            {
                Categories.Clear();
                foreach (var category in result.Data)
                {
                    Categories.Add(category);
                }

                // Ýlk kategoriyi seç
                if (Categories.Any())
                {
                    SelectedCategory = Categories.First();
                }
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
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                // Kullanýcý kontrolü
                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Hata",
                        "Oturum bulunamadý. Lütfen tekrar giriþ yapýn.",
                        "Tamam"
                    );
                    return;
                }

                // Kategori kontrolü
                if (SelectedCategory == null)
                {
                    ErrorMessage = "Lütfen bir kategori seçin";
                    return;
                }

                // Request oluþtur
                var request = new ProductRequest
                {
                    Title = Title,
                    Description = Description,
                    CategoryId = SelectedCategory.CategoryId,
                    Condition = SelectedCondition,
                    Type = SelectedType,
                    Price = Price,
                    Location = Location,
                    ExchangePreference = ExchangePreference,
                    ImagePaths = ImagePaths.ToList()
                };

                // Ürünü ekle
                var result = await _productService.AddProductAsync(request, currentUser);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Baþarýlý",
                        result.Message,
                        "Tamam"
                    );

                    // Formu temizle
                    ClearForm();

                    // Ana sayfaya dön
                    await Shell.Current.GoToAsync("..");
                }
                else
                {
                    ErrorMessage = result.Message;
                    if (result.Errors != null && result.Errors.Any())
                    {
                        ErrorMessage += "\n" + string.Join("\n", result.Errors);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ürün eklenirken hata oluþtu: {ex.Message}";
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