using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.IO; // Path.GetFileName i�in eklendi

namespace KamPay.ViewModels
{
    public partial class AddProductViewModel : ObservableObject
    {
        private readonly IProductService _productService;
        private readonly IAuthenticationService _authService;
        private readonly IUserProfileService _userProfileService;
        private readonly ICategoryService _categoryService; // YEN�: Kategoriler i�in

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

        // CONSTRUCTOR'I G�NCELLEYEL�M
        public AddProductViewModel(
            IProductService productService,
            IAuthenticationService authService,
            IUserProfileService userProfileService, // HATA D�ZELTMES�: Geri eklendi
            IStorageService storageService, // Eklendi
            ICategoryService categoryService) // Eklendi (IUserProfileService yerine �imdilik bu daha kritik)
        {
            _productService = productService;
            _authService = authService;
            _userProfileService = userProfileService; // HATA D�ZELTMES�: Atama yap�ld�
            _categoryService = categoryService;

            // Varsay�lan de�erler
            SelectedCondition = ProductCondition.Iyi;
            SelectedType = ProductType.Satis;
            ShowPriceField = true;
            ShowExchangeField = false;

            // Constructor i�inde async �a��rmak yerine, komutu sayfa a��ld���nda tetikleyece�iz.
            // VEYA komutu do�rudan burada �a��rabiliriz:
            // LoadCategoriesCommand.Execute(null); // Sayfa a��lmadan y�klemeye ba�lar
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


        [RelayCommand]
        private async Task LoadCategoriesAsync()
        {
            if (IsLoading) return;
            try
            {
                IsLoading = true;
                var categoryList = await _categoryService.GetCategoriesAsync(); // Do�ru servisten �a��r�yoruz

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
                ErrorMessage = $"Kategoriler y�klenemedi: {ex.Message}";
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
            if (IsLoading) return;

            // Gerekli alanlar�n dolu oldu�unu do�rulay�n
            if (string.IsNullOrWhiteSpace(Title) || SelectedCategory == null || !ImagePaths.Any())
            {
                ErrorMessage = "L�tfen ba�l�k, kategori ve en az bir resim ekledi�inizden emin olun.";
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
                    await Shell.Current.DisplayAlert("Hata", "Oturum bulunamad�. L�tfen tekrar giri� yap�n.", "Tamam");
                    return;
                }

                // Modelinize uygun 'ProductRequest' nesnesini olu�turuyoruz.
                // Bu nesne, resimlerin sadece lokal yollar�n� i�erir.
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
                    ImagePaths = this.ImagePaths.ToList(), // Lokal dosya yollar�n� servise g�nderiyoruz
                    IsForSurpriseBox = this.IsForSurpriseBox
                };

                // Servisi, bekledi�i do�ru parametrelerle �a��r�yoruz.
                // Resim y�kleme i�ini bu metot kendi i�inde halledecektir.
                var result = await _productService.AddProductAsync(request, currentUser);

                if (result.Success)
                {
                    await _userProfileService.AddPointsForAction(currentUser.UserId, UserAction.AddProduct);
                    await Shell.Current.DisplayAlert("Ba�ar�l�", "�r�n�n�z ba�ar�yla eklendi!", "Harika!");
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
                ErrorMessage = $"�r�n kaydedilirken beklenmedik bir hata olu�tu: {ex.Message}";
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