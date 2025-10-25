using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.Models;
using KamPay.Services;
using KamPay.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics; // Hata ayıklama için eklendi
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
        private readonly ICategoryService _categoryService; // DOĞRU SERVİS EKLENDİ
        private IDisposable _notificationSubscription;
        private IDisposable _productSubscription;
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);
        private CancellationTokenSource _searchCancellationTokenSource; // YENİ EKLENDİ
        private readonly CacheManager<List<Product>> _cacheManager = new();
        private const string CACHE_KEY = "all_products";                                                               // ✅ YENİ METOD EKLE - Sonsuz scroll için
        private string _lastLoadedKey;
        private bool _isLoadingMore;

        // Tüm ürünlerin tutulduğu ana liste (filtreleme için)
        private List<Product> _allProducts = new();

        // Arayüze bağlanan ve sadece filtrelenmiş ürünleri gösteren liste
        public ObservableCollection<Product> Products { get; } = new();
        public ObservableCollection<Category> Categories { get; } = new();

        #region Observable Properties (Arayüzle İletişim Kuran Özellikler)
        [ObservableProperty] private string userId;
        [ObservableProperty] private bool isLoading = true;
        [ObservableProperty] private bool hasUnreadNotifications;
        [ObservableProperty] private string searchText;
        [ObservableProperty] private Category selectedCategory;
        [ObservableProperty] private ProductType? selectedType;
        [ObservableProperty] private ProductSortOption selectedSortOption;
        [ObservableProperty] private bool showFilterPanel;
        [ObservableProperty] private string emptyMessage = "Henüz ürün eklenmemiş";
        [ObservableProperty] private bool _isRefreshing;

        #endregion

        public List<ProductSortOption> SortOptions { get; } = Enum.GetValues(typeof(ProductSortOption)).Cast<ProductSortOption>().ToList();

        public ProductListViewModel(IProductService productService, IAuthenticationService authService, ICategoryService categoryService) // ICategoryService'i buraya ekleyin)
        {
            _productService = productService;
            _authService = authService;
            _categoryService = categoryService; // Gelen servisi atayın
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

        #region Veri Yükleme ve Filtreleme Mantığı

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
                EmptyMessage = "Arama kriterlerinize uygun ürün bulunamadı";
                StartListeningForProducts();
            }
        }

        private void StartListeningForProducts()
        {
            // ÇÖZÜM: Sadece ana ürün listesi boşsa yükleme animasyonunu göster.
            // Bu, sadece ilk yüklemede veya filtre tamamen boşaldığında çalışır.
            if (!_allProducts.Any())
            {
                IsLoading = true;
            }

            _productSubscription?.Dispose();
            // NOT: _allProducts listesini burada temizlemiyoruz.
            // Çünkü geri dönüldüğünde eski veriyi göstermek istiyoruz.
            // Dinleyici yeni verileri getirdikçe liste güncellenecektir.

            _productSubscription = _firebaseClient
                .Child(Constants.ProductsCollection)
                .AsObservable<Product>()
                .Subscribe(e =>
                {
                    // Hata Ayıklama Mesajı
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
                        Debug.WriteLine("[DEBUG] Firebase dinleyicisi zaman aşımına uğradı.");
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

            EmptyMessage = Products.Any() ? string.Empty : "Arama kriterlerinize uygun ürün bulunamadı";
        }

        async partial void OnSearchTextChanged(string value)
        {
            // Önceki gecikme görevini iptal et (kullanıcı hala yazıyor)
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Kullanıcının yazmayı bırakması için 300 milisaniye bekle
                await Task.Delay(300, _searchCancellationTokenSource.Token);

                // Bekleme süresi dolduysa ve iptal edilmediyse, filtrelemeyi şimdi yap
                ApplyFilters();
            }
            catch (TaskCanceledException)
            {
                // Bu hata, kullanıcı hızlı yazdığında beklenen bir durumdur.
                // Görevin iptal edildiğini gösterir, görmezden gelebiliriz.
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


        // ✅ RefreshCommand'i güncelle
        [RelayCommand]
        private async Task RefreshProductsAsync()
        {
            IsRefreshing = true;

            try
            {
                // Cache'i temizle
                _cacheManager.Clear();
                _lastLoadedKey = null;

                // Yeniden yükle
                await LoadProductsAsync();
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        // ✅ LoadProductsAsync'e cache kontrolü ekle
        [RelayCommand]
        private async Task LoadProductsAsync()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;

                // 🔥 Önce cache'e bak
                if (_cacheManager.TryGet(CACHE_KEY, out var cachedProducts))
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        Products.Clear();
                        foreach (var p in cachedProducts) Products.Add(p);
                    });
                    return;
                }

                // Cache yoksa network'ten al
                var productsResult = await Task.Run(async () =>
                {
                    var filter = new ProductFilter
                    {
                        SearchText = SearchText,
                        CategoryId = SelectedCategory?.CategoryId,
                        OnlyActive = true,
                        SortBy = ProductSortOption.Newest
                    };

                    return await _productService.GetProductsPagedAsync(20, null, filter);
                });

                if (productsResult.Success)
                {
                    // Cache'e kaydet
                    _cacheManager.Set(CACHE_KEY, productsResult.Data, TimeSpan.FromMinutes(3));

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        Products.Clear();
                        foreach (var product in productsResult.Data)
                        {
                            Products.Add(product);
                        }
                    });

                    if (productsResult.Data.Any())
                        _lastLoadedKey = productsResult.Data.Last().ProductId;
                }
            }
            catch (Exception ex)
            {
                EmptyMessage = $"Hata: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task LoadMoreProductsAsync()
        {
            if (_isLoadingMore || string.IsNullOrEmpty(_lastLoadedKey)) return;

            try
            {
                _isLoadingMore = true;

                var moreProducts = await Task.Run(async () =>
                {
                    var filter = new ProductFilter
                    {
                        SearchText = SearchText,
                        CategoryId = SelectedCategory?.CategoryId,
                        OnlyActive = true
                    };

                    return await _productService.GetProductsPagedAsync(20, _lastLoadedKey, filter);
                });

                if (moreProducts.Success && moreProducts.Data.Any())
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        foreach (var product in moreProducts.Data)
                        {
                            Products.Add(product);
                        }
                    });

                    _lastLoadedKey = moreProducts.Data.Last().ProductId;
                }
            }
            finally
            {
                _isLoadingMore = false;
            }
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

        #region Yardımcı Metotlar
        private async Task LoadCategoriesAsync()
        {
            // Yeni ve doğru servisi kullanıyoruz
            var categoryList = await _categoryService.GetCategoriesAsync();

            if (categoryList != null)
            {
                // Mevcut XAML tasarımınız "Tümü" butonunu kendi içinde barındırdığı için,
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
            return option switch { ProductSortOption.Newest => "En Yeni", ProductSortOption.Oldest => "En Eski", ProductSortOption.PriceAsc => "Fiyat (Artan)", ProductSortOption.PriceDesc => "Fiyat (Azalan)", ProductSortOption.MostViewed => "En Çok Görüntülenen", ProductSortOption.MostFavorited => "En Çok Favorilenen", _ => "Sırala" };
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