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
        private readonly IUserProfileService _userProfileService;

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

        public AddProductViewModel(IProductService productService, IAuthenticationService authService, IUserProfileService userProfileService)
        {
            _productService = productService;
            _authService = authService;
            _userProfileService = userProfileService;

            // Varsay�lan de�erler
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

            // "Ba���" se�ene�i de�i�ti�inde aray�z�n g�ncellenmesi i�in haber ver
            OnPropertyChanged(nameof(IsDonationTypeSelected));

            if (value != ProductType.Satis)
            {
                Price = 0;
            }
            // E�er se�ilen t�r "Ba���" de�ilse, S�rpriz Kutu se�ene�ini de s�f�rla
            if (value != ProductType.Bagis)
            {
                IsForSurpriseBox = false;
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

                // �lk kategoriyi se�
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
                // MAUI Media Picker kullanarak foto�raf se�
                var photos = await MediaPicker.PickPhotoAsync(new MediaPickerOptions
                {
                    Title = "�r�n G�rseli Se�in"
                });

                if (photos != null)
                {
                    // Maksimum g�rsel say�s� kontrol�
                    if (ImagePaths.Count >= 5)
                    {
                        await Application.Current.MainPage.DisplayAlert(
                            "Uyar�",
                            "En fazla 5 g�rsel ekleyebilirsiniz",
                            "Tamam"
                        );
                        return;
                    }

                    // G�rseli listeye ekle
                    ImagePaths.Add(photos.FullPath);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"G�rsel se�ilirken hata olu�tu: {ex.Message}";
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
                    // Konum bilgisini string'e �evir
                    // Ger�ek uygulamada Geocoding kullanarak adres al�nabilir
                    Location = $"Kamp�s ({location.Latitude:F4}, {location.Longitude:F4})";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Konum al�namad�: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SaveProductAsync()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                // Kullan�c� kontrol�
                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Hata",
                        "Oturum bulunamad�. L�tfen tekrar giri� yap�n.",
                        "Tamam"
                    );
                    return;
                }

                // Kategori kontrol�
                if (SelectedCategory == null)
                {
                    ErrorMessage = "L�tfen bir kategori se�in";
                    return;
                }

                // Request olu�tur
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
                    ImagePaths = ImagePaths.ToList(),
                    IsForSurpriseBox = this.IsForSurpriseBox
                };

                // �r�n� ekle
                var result = await _productService.AddProductAsync(request, currentUser);

                if (result.Success)
                {
                    await _userProfileService.AddPointsForAction(currentUser.UserId, UserAction.AddProduct);

                    await Application.Current.MainPage.DisplayAlert(
                        "Ba�ar�l�",
                        result.Message,
                        "Tamam"
                    );

                    // Formu temizle
                    ClearForm();

                    // Ana sayfaya d�n
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
                ErrorMessage = $"�r�n eklenirken hata olu�tu: {ex.Message}";
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
                "�ptal",
                "�r�n eklemeyi iptal etmek istedi�inize emin misiniz?",
                "Evet",
                "Hay�r"
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