using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.Models;
using KamPay.Services;
using KamPay.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using KamPay.Helpers;
using Firebase.Database;
using Firebase.Database.Query;
using System.Reactive.Linq; // Bu satýr önemli!

namespace KamPay.ViewModels

{
    public partial class ProductListViewModel : ObservableObject
    {
        private readonly IProductService _productService;

        private readonly IAuthenticationService _authService;
        private IDisposable _notificationSubscription; // Dinleyiciyi durdurabilmek için
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);


        [ObservableProperty]
        private bool isLoading;



        [ObservableProperty]
        private bool hasUnreadNotifications;

        [ObservableProperty]
        private bool isRefreshing;

        [ObservableProperty]
        private string searchText;

        [ObservableProperty]
        private Category selectedCategory;

        [ObservableProperty]
        private ProductType? selectedType;

        [ObservableProperty]
        private ProductSortOption selectedSortOption;

        [ObservableProperty]
        private bool showFilterPanel;

        [ObservableProperty]
        private string emptyMessage = "Henüz ürün eklenmemiþ";

        public ObservableCollection<Product> Products { get; } = new();
        public ObservableCollection<Category> Categories { get; } = new();

        public List<ProductSortOption> SortOptions { get; } = new()
        {
            ProductSortOption.Newest,
            ProductSortOption.Oldest,
            ProductSortOption.PriceAsc,
            ProductSortOption.PriceDesc,
            ProductSortOption.MostViewed
        };

        public ProductListViewModel(IProductService productService, IAuthenticationService authService)
        {
            _productService = productService;
            _authService = authService; // DI ile servisi alýyoruz
            SelectedSortOption = ProductSortOption.Newest;

            LoadDataAsync();
            StartListeningForNotifications(); // Dinleyiciyi baþlat

            // ===== YENÝ EKLENDÝ: Mesajý Dinlemeye Baþla =====
            WeakReferenceMessenger.Default.Register<FavoriteCountChangedMessage>(this, (r, m) =>
            {
                // Mesaj geldiðinde çalýþacak kod
                var productToUpdate = Products.FirstOrDefault(p => p.ProductId == m.Value.ProductId);
                if (productToUpdate != null)
                {
                    // Ürünün favori sayýsýný güncelle.
                    // Product sýnýfý ObservableObject'ten türediði için UI otomatik olarak yenilenecektir.
                    productToUpdate.FavoriteCount = m.Value.FavoriteCount;
                }
            });

            WeakReferenceMessenger.Default.Register<UnreadGeneralNotificationStatusMessage>(this, (r, m) =>
            {
                // Mesaj geldiðinde kýrmýzý noktayý göster/gizle
                HasUnreadNotifications = m.Value;
            });

        }

        // ===== YENÝ METOT EKLENDÝ =====
        private async void StartListeningForNotifications()
        {
            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null) return;

            // Önceki dinleyiciyi durdur (varsa)
            _notificationSubscription?.Dispose();

            // Firebase'de mevcut kullanýcýya ait okunmamýþ bildirim var mý diye kontrol et
            var initialCheck = await _firebaseClient
                .Child(Constants.NotificationsCollection)
                .OrderBy("UserId")
                .EqualTo(currentUser.UserId)
                .OnceAsync<Notification>();

            // ===== GÜNCELLEME: Sadece mesaj olmayan okunmamýþ bildirim var mý diye kontrol et =====
            if (initialCheck.Any(n => !n.Object.IsRead && n.Object.Type != NotificationType.NewMessage))
            {
                HasUnreadNotifications = true;
            }

            // Gerçek zamanlý olarak yeni eklenen bildirimleri dinle
            _notificationSubscription = _firebaseClient
                .Child(Constants.NotificationsCollection)
                .OrderBy("UserId")
                .EqualTo(currentUser.UserId)
                .AsObservable<Notification>()
                .Where(e => e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate) // Sadece yeni veya güncellenenler
                .Subscribe(entry =>
                {
                    // Yeni ve okunmamýþ bir bildirim geldiyse kýrmýzý noktayý göster
                    if (entry.Object != null && !entry.Object.IsRead && entry.Object.Type != NotificationType.NewMessage)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            HasUnreadNotifications = true;
                        });
                    }
                });
        }


        private async void LoadDataAsync()
        {
            await LoadCategoriesAsync();
            await LoadProductsAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            var result = await _productService.GetCategoriesAsync();
            if (result.Success && result.Data != null)
            {
                Categories.Clear();

                // "Tümü" kategorisi ekle
                Categories.Add(new Category { CategoryId = null, Name = "Tümü", IconName = "apps.png" });

                foreach (var category in result.Data)
                {
                    Categories.Add(category);
                }
            }
        }

        [RelayCommand]
        private async Task LoadProductsAsync()
        {
            try
            {
                IsLoading = true;

                // Filter oluþtur
                var filter = new ProductFilter
                {
                    SearchText = SearchText,
                    CategoryId = SelectedCategory?.CategoryId,
                    Type = SelectedType,
                    SortBy = SelectedSortOption,
                    OnlyActive = true,
                    ExcludeSold = true
                };

                var result = await _productService.GetAllProductsAsync(filter);

                if (result.Success && result.Data != null)
                {
                    Products.Clear();
                    foreach (var product in result.Data)
                    {
                        Products.Add(product);
                    }

                    EmptyMessage = Products.Any()
                        ? string.Empty
                        : "Arama kriterlerinize uygun ürün bulunamadý";
                }
                else
                {
                    EmptyMessage = "Ürünler yüklenemedi";
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Hata",
                    $"Ürünler yüklenirken hata oluþtu: {ex.Message}",
                    "Tamam"
                );
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task RefreshProductsAsync()
        {
            IsRefreshing = true;
            await LoadProductsAsync();
            IsRefreshing = false;
        }

        [RelayCommand]
        private async Task SearchAsync()
        {
            await LoadProductsAsync();
        }

        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
            _ = LoadProductsAsync();
        }

        [RelayCommand]
        private void ToggleFilterPanel()
        {
            ShowFilterPanel = !ShowFilterPanel;
        }

        [RelayCommand]
        private async Task ApplyFiltersAsync()
        {
            ShowFilterPanel = false;
            await LoadProductsAsync();
        }

        [RelayCommand]
        private async Task ClearFiltersAsync()
        {
            SelectedCategory = Categories.FirstOrDefault();
            SelectedType = null;
            SelectedSortOption = ProductSortOption.Newest;
            SearchText = string.Empty;

            await LoadProductsAsync();
        }

        [RelayCommand]
        private async Task SelectCategoryAsync(Category category)
        {
            SelectedCategory = category;
            await LoadProductsAsync();
        }

        [RelayCommand]
        private async Task ProductTappedAsync(Product product)
        {
            if (product == null) return;

            // Görüntülenme sayýsýný artýr
            await _productService.IncrementViewCountAsync(product.ProductId);

            // Detay sayfasýna git
            await Shell.Current.GoToAsync($"ProductDetailPage?productId={product.ProductId}");
        }

        [RelayCommand]
        private async Task GoToNotificationsAsync()
        {
            // ===== GÜNCELLENDÝ: Zile basýldýðýnda kýrmýzý noktayý kaldýr =====
            if (HasUnreadNotifications)
            {
                HasUnreadNotifications = false;
            }
            // AppShell.xaml.cs içinde kaydettiðimiz NotificationsPage rotasýna git
            await Shell.Current.GoToAsync(nameof(NotificationsPage));
        }
        [RelayCommand]
        private async Task GoToAddProductAsync()
        {
            await Shell.Current.GoToAsync("AddProductPage");
        }

        partial void OnSelectedSortOptionChanged(ProductSortOption value)
        {
            _ = LoadProductsAsync();
        }

        partial void OnSelectedTypeChanged(ProductType? value)
        {
            _ = LoadProductsAsync();
        }

        // Kategori adý için helper method
        public string GetSortOptionText(ProductSortOption option)
        {
            return option switch
            {
                ProductSortOption.Newest => "En Yeni",
                ProductSortOption.Oldest => "En Eski",
                ProductSortOption.PriceAsc => "Fiyat (Düþükten Yükseðe)",
                ProductSortOption.PriceDesc => "Fiyat (Yüksekten Düþüðe)",
                ProductSortOption.MostViewed => "En Çok Görüntülenen",
                ProductSortOption.MostFavorited => "En Çok Favorilenen",
                _ => "Sýrala"
            };
        }
    }
}