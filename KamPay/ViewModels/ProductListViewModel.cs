using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.Models;
using KamPay.Services;
using KamPay.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics; // Hata ayýklama için eklendi
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;

namespace KamPay.ViewModels
{
    [QueryProperty(nameof(UserId), "userId")]
    public partial class ProductListViewModel : ObservableObject, IDisposable
    {
        private readonly IProductService _productService;
        private readonly IAuthenticationService _authService;
        private readonly ICategoryService _categoryService; // DOÐRU SERVÝS EKLENDÝ
        private IDisposable _notificationSubscription;
        private IDisposable _productSubscription;
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);
        private CancellationTokenSource _searchCancellationTokenSource; // YENÝ EKLENDÝ

        // Tüm ürünlerin tutulduðu ana liste (filtreleme için)
        private List<Product> _allProducts = new();

        // Arayüze baðlanan ve sadece filtrelenmiþ ürünleri gösteren liste
        public ObservableCollection<Product> Products { get; } = new();
        public ObservableCollection<Category> Categories { get; } = new();

        #region Observable Properties (Arayüzle Ýletiþim Kuran Özellikler)
        [ObservableProperty] private string userId;
        [ObservableProperty] private bool isLoading = true;
        [ObservableProperty] private bool hasUnreadNotifications;
        [ObservableProperty] private string searchText;
        [ObservableProperty] private Category selectedCategory;
        [ObservableProperty] private ProductType? selectedType;
        [ObservableProperty] private ProductSortOption selectedSortOption;
        [ObservableProperty] private bool showFilterPanel;
        [ObservableProperty] private string emptyMessage = "Henüz ürün eklenmemiþ";
        #endregion

        public List<ProductSortOption> SortOptions { get; } = Enum.GetValues(typeof(ProductSortOption)).Cast<ProductSortOption>().ToList();

        public ProductListViewModel(IProductService productService, IAuthenticationService authService, ICategoryService categoryService) // ICategoryService'i buraya ekleyin)
        {
            _productService = productService;
            _authService = authService;
            _categoryService = categoryService; // Gelen servisi atayýn
            SelectedSortOption = ProductSortOption.Newest;

            WeakReferenceMessenger.Default.Register<FavoriteCountChangedMessage>(this, (r, m) =>
            {
                var productToUpdate = _allProducts.FirstOrDefault(p => p.ProductId == m.Value.ProductId);
                if (productToUpdate != null) productToUpdate.FavoriteCount = m.Value.FavoriteCount;
            });
            WeakReferenceMessenger.Default.Register<UnreadGeneralNotificationStatusMessage>(this, (r, m) => { HasUnreadNotifications = m.Value; });

            InitializeViewModel();
        }

        public async void InitializeViewModel()
        {
            await LoadCategoriesAsync();
            StartListeningForNotifications();

            if (string.IsNullOrEmpty(UserId))
            {
                StartListeningForProducts();
            }
        }

        #region Veri Yükleme ve Filtreleme Mantýðý

        async partial void OnUserIdChanged(string value)
        {
            _productSubscription?.Dispose();
            _allProducts.Clear();
            Products.Clear();

            if (!string.IsNullOrEmpty(value))
            {
                IsLoading = true;
                EmptyMessage = "Henüz ürün eklemediniz";
                var result = await _productService.GetUserProductsAsync(value);
                if (result.Success && result.Data != null)
                {
                    _allProducts = result.Data;
                    ApplyFilters();
                }
                IsLoading = false;
            }
            else
            {
                EmptyMessage = "Arama kriterlerinize uygun ürün bulunamadý";
                StartListeningForProducts();
            }
        }

        private void StartListeningForProducts()
        {
            // ÇÖZÜM: Sadece ana ürün listesi boþsa yükleme animasyonunu göster.
            // Bu, sadece ilk yüklemede veya filtre tamamen boþaldýðýnda çalýþýr.
            if (!_allProducts.Any())
            {
                IsLoading = true;
            }

            _productSubscription?.Dispose();
            // NOT: _allProducts listesini burada temizlemiyoruz.
            // Çünkü geri dönüldüðünde eski veriyi göstermek istiyoruz.
            // Dinleyici yeni verileri getirdikçe liste güncellenecektir.

            _productSubscription = _firebaseClient
                .Child(Constants.ProductsCollection)
                .AsObservable<Product>()
                .Subscribe(e =>
                {
                    // Hata Ayýklama Mesajý
                    Debug.WriteLine($"[DEBUG] Firebase Event: {e.EventType}, Key: {e.Key}");

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var product = e.Object;
                        product.ProductId = e.Key;
                        var existingProduct = _allProducts.FirstOrDefault(p => p.ProductId == product.ProductId);

                        if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate)
                        {
                            if (existingProduct != null)
                            {
                                var index = _allProducts.IndexOf(existingProduct);
                                _allProducts[index] = product;
                            }
                            else
                            {
                                _allProducts.Insert(0, product);
                            }
                        }
                        else if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.Delete)
                        {
                            if (existingProduct != null) _allProducts.Remove(existingProduct);
                        }

                        ApplyFilters();
                        IsLoading = false;
                    });
                }, ex => {
                    if (ex is TimeoutException)
                    {
                        Debug.WriteLine("[DEBUG] Firebase dinleyicisi zaman aþýmýna uðradý.");
                    }
                    else
                    {
                        Debug.WriteLine($"[HATA] Firebase dinleyicisinde sorun: {ex.Message}");
                    }
                    // Her durumda yükleme animasyonunu kapat
                    MainThread.BeginInvokeOnMainThread(() => { IsLoading = false; });
                });
        }

        private void ApplyFilters()
        {
            IEnumerable<Product> filtered = _allProducts.Where(p => p.IsActive && !p.IsSold);

            if (SelectedCategory != null && !string.IsNullOrEmpty(SelectedCategory.CategoryId))
            {
                filtered = filtered.Where(p => p.CategoryId == SelectedCategory.CategoryId);
            }

            if (!string.IsNullOrEmpty(SearchText))
            {
                filtered = filtered.Where(p => p.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            if (SelectedType.HasValue)
            {
                filtered = filtered.Where(p => p.Type == SelectedType.Value);
            }

            filtered = SelectedSortOption switch
            {
                ProductSortOption.Oldest => filtered.OrderBy(p => p.CreatedAt),
                ProductSortOption.PriceAsc => filtered.OrderBy(p => p.Price),
                ProductSortOption.PriceDesc => filtered.OrderByDescending(p => p.Price),
                ProductSortOption.MostViewed => filtered.OrderByDescending(p => p.ViewCount),
                ProductSortOption.MostFavorited => filtered.OrderByDescending(p => p.FavoriteCount),
                _ => filtered.OrderByDescending(p => p.CreatedAt),
            };

            var filteredList = filtered.ToList();

            Products.Clear();
            foreach (var product in filteredList)
            {
                Products.Add(product);
            }

            EmptyMessage = Products.Any() ? string.Empty : "Arama kriterlerinize uygun ürün bulunamadý";
        }

        async partial void OnSearchTextChanged(string value)
        {
            // Önceki gecikme görevini iptal et (kullanýcý hala yazýyor)
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Kullanýcýnýn yazmayý býrakmasý için 300 milisaniye bekle
                await Task.Delay(300, _searchCancellationTokenSource.Token);

                // Bekleme süresi dolduysa ve iptal edilmediyse, filtrelemeyi þimdi yap
                ApplyFilters();
            }
            catch (TaskCanceledException)
            {
                // Bu hata, kullanýcý hýzlý yazdýðýnda beklenen bir durumdur.
                // Görevin iptal edildiðini gösterir, görmezden gelebiliriz.
                Debug.WriteLine("Arama ertelendi (debounced).");
            }
        }
        partial void OnSelectedCategoryChanged(Category value) => ApplyFilters();
        partial void OnSelectedSortOptionChanged(ProductSortOption value) => ApplyFilters();
        partial void OnSelectedTypeChanged(ProductType? value) => ApplyFilters();

        #endregion

        #region Komutlar
        [RelayCommand]
        private void ToggleFilterPanel() => ShowFilterPanel = !ShowFilterPanel;

        [RelayCommand]
        private void ApplyFiltersCommand()
        {
            ShowFilterPanel = false;
            ApplyFilters();
        }

        [RelayCommand]
        private void ClearFilters()
        {
            SelectedCategory = Categories.FirstOrDefault();
            SelectedType = null;
            SelectedSortOption = ProductSortOption.Newest;
            SearchText = string.Empty;
        }

        [RelayCommand]
        private void CategoryTapped(Category category)
        {
            SelectedCategory = category;
        }

        [RelayCommand]
        private async Task GoToSurpriseBox()
        {
            await Shell.Current.GoToAsync(nameof(SurpriseBoxPage));
        }

        [RelayCommand]
        private async Task ProductTappedAsync(Product product)
        {
            if (product is null || product.IsSold) return;
            await Shell.Current.GoToAsync($"{nameof(ProductDetailPage)}?ProductId={product.ProductId}");
        }

        [RelayCommand]
        private async Task GoToNotificationsAsync()
        {
            if (HasUnreadNotifications) HasUnreadNotifications = false;
            await Shell.Current.GoToAsync(nameof(NotificationsPage));
        }

        [RelayCommand]
        private async Task GoToAddProductAsync()
        {
            await Shell.Current.GoToAsync(nameof(AddProductPage));
        }
        #endregion

        #region Yardýmcý Metotlar
        private async Task LoadCategoriesAsync()
        {
            // Yeni ve doðru servisi kullanýyoruz
            var categoryList = await _categoryService.GetCategoriesAsync();

            if (categoryList != null)
            {
                // Mevcut XAML tasarýmýnýz "Tümü" butonunu kendi içinde barýndýrdýðý için,
                // ViewModel'de tekrar eklememize gerek yok. Listeyi temizleyip gelen verilerle doldurmak yeterli.
                Categories.Clear();
                foreach (var category in categoryList)
                {
                    Categories.Add(category);
                }
            }
        }

        private async void StartListeningForNotifications()
        {
            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null) return;
            _notificationSubscription?.Dispose();
            var initialCheck = await _firebaseClient.Child(Constants.NotificationsCollection).OrderBy("UserId").EqualTo(currentUser.UserId).OnceAsync<Notification>();
            if (initialCheck.Any(n => !n.Object.IsRead && n.Object.Type != NotificationType.NewMessage))
            {
                HasUnreadNotifications = true;
            }
            _notificationSubscription = _firebaseClient.Child(Constants.NotificationsCollection).OrderBy("UserId").EqualTo(currentUser.UserId).AsObservable<Notification>().Where(e => e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate).Subscribe(entry =>
            {
                if (entry.Object != null && !entry.Object.IsRead && entry.Object.Type != NotificationType.NewMessage)
                {
                    MainThread.BeginInvokeOnMainThread(() => { HasUnreadNotifications = true; });
                }
            });
        }

        public string GetSortOptionText(ProductSortOption option)
        {
            return option switch { ProductSortOption.Newest => "En Yeni", ProductSortOption.Oldest => "En Eski", ProductSortOption.PriceAsc => "Fiyat (Artan)", ProductSortOption.PriceDesc => "Fiyat (Azalan)", ProductSortOption.MostViewed => "En Çok Görüntülenen", ProductSortOption.MostFavorited => "En Çok Favorilenen", _ => "Sýrala" };
        }

        public void Dispose()
        {
            _notificationSubscription?.Dispose();
            _productSubscription?.Dispose();
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
        #endregion
    }
}