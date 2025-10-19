using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.Models;
using KamPay.Services;
using KamPay.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics; // Hata ay�klama i�in eklendi
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
        private readonly ICategoryService _categoryService; // DO�RU SERV�S EKLEND�
        private IDisposable _notificationSubscription;
        private IDisposable _productSubscription;
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);
        private CancellationTokenSource _searchCancellationTokenSource; // YEN� EKLEND�

        // T�m �r�nlerin tutuldu�u ana liste (filtreleme i�in)
        private List<Product> _allProducts = new();

        // Aray�ze ba�lanan ve sadece filtrelenmi� �r�nleri g�steren liste
        public ObservableCollection<Product> Products { get; } = new();
        public ObservableCollection<Category> Categories { get; } = new();

        #region Observable Properties (Aray�zle �leti�im Kuran �zellikler)
        [ObservableProperty] private string userId;
        [ObservableProperty] private bool isLoading = true;
        [ObservableProperty] private bool hasUnreadNotifications;
        [ObservableProperty] private string searchText;
        [ObservableProperty] private Category selectedCategory;
        [ObservableProperty] private ProductType? selectedType;
        [ObservableProperty] private ProductSortOption selectedSortOption;
        [ObservableProperty] private bool showFilterPanel;
        [ObservableProperty] private string emptyMessage = "Hen�z �r�n eklenmemi�";
        #endregion

        public List<ProductSortOption> SortOptions { get; } = Enum.GetValues(typeof(ProductSortOption)).Cast<ProductSortOption>().ToList();

        public ProductListViewModel(IProductService productService, IAuthenticationService authService, ICategoryService categoryService) // ICategoryService'i buraya ekleyin)
        {
            _productService = productService;
            _authService = authService;
            _categoryService = categoryService; // Gelen servisi atay�n
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

        #region Veri Y�kleme ve Filtreleme Mant���

        async partial void OnUserIdChanged(string value)
        {
            _productSubscription?.Dispose();
            _allProducts.Clear();
            Products.Clear();

            if (!string.IsNullOrEmpty(value))
            {
                IsLoading = true;
                EmptyMessage = "Hen�z �r�n eklemediniz";
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
                EmptyMessage = "Arama kriterlerinize uygun �r�n bulunamad�";
                StartListeningForProducts();
            }
        }

        private void StartListeningForProducts()
        {
            // ��Z�M: Sadece ana �r�n listesi bo�sa y�kleme animasyonunu g�ster.
            // Bu, sadece ilk y�klemede veya filtre tamamen bo�ald���nda �al���r.
            if (!_allProducts.Any())
            {
                IsLoading = true;
            }

            _productSubscription?.Dispose();
            // NOT: _allProducts listesini burada temizlemiyoruz.
            // ��nk� geri d�n�ld���nde eski veriyi g�stermek istiyoruz.
            // Dinleyici yeni verileri getirdik�e liste g�ncellenecektir.

            _productSubscription = _firebaseClient
                .Child(Constants.ProductsCollection)
                .AsObservable<Product>()
                .Subscribe(e =>
                {
                    // Hata Ay�klama Mesaj�
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
                        Debug.WriteLine("[DEBUG] Firebase dinleyicisi zaman a��m�na u�rad�.");
                    }
                    else
                    {
                        Debug.WriteLine($"[HATA] Firebase dinleyicisinde sorun: {ex.Message}");
                    }
                    // Her durumda y�kleme animasyonunu kapat
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

            EmptyMessage = Products.Any() ? string.Empty : "Arama kriterlerinize uygun �r�n bulunamad�";
        }

        async partial void OnSearchTextChanged(string value)
        {
            // �nceki gecikme g�revini iptal et (kullan�c� hala yaz�yor)
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Kullan�c�n�n yazmay� b�rakmas� i�in 300 milisaniye bekle
                await Task.Delay(300, _searchCancellationTokenSource.Token);

                // Bekleme s�resi dolduysa ve iptal edilmediyse, filtrelemeyi �imdi yap
                ApplyFilters();
            }
            catch (TaskCanceledException)
            {
                // Bu hata, kullan�c� h�zl� yazd���nda beklenen bir durumdur.
                // G�revin iptal edildi�ini g�sterir, g�rmezden gelebiliriz.
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

        #region Yard�mc� Metotlar
        private async Task LoadCategoriesAsync()
        {
            // Yeni ve do�ru servisi kullan�yoruz
            var categoryList = await _categoryService.GetCategoriesAsync();

            if (categoryList != null)
            {
                // Mevcut XAML tasar�m�n�z "T�m�" butonunu kendi i�inde bar�nd�rd��� i�in,
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
            return option switch { ProductSortOption.Newest => "En Yeni", ProductSortOption.Oldest => "En Eski", ProductSortOption.PriceAsc => "Fiyat (Artan)", ProductSortOption.PriceDesc => "Fiyat (Azalan)", ProductSortOption.MostViewed => "En �ok G�r�nt�lenen", ProductSortOption.MostFavorited => "En �ok Favorilenen", _ => "S�rala" };
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