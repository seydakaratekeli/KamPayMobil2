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
using System.Reactive.Linq;

namespace KamPay.ViewModels
{
    public partial class ProductListViewModel : ObservableObject, IDisposable
    {
        private readonly IProductService _productService;
        private readonly IAuthenticationService _authService;
        private IDisposable _notificationSubscription;
        private IDisposable _productSubscription;
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);

        [ObservableProperty]
        private bool isLoading = true;

        [ObservableProperty]
        private bool hasUnreadNotifications;

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
            _authService = authService;
            SelectedSortOption = ProductSortOption.Newest;

            StartListeners();

            WeakReferenceMessenger.Default.Register<FavoriteCountChangedMessage>(this, (r, m) =>
            {
                var productToUpdate = Products.FirstOrDefault(p => p.ProductId == m.Value.ProductId);
                if (productToUpdate != null)
                {
                    productToUpdate.FavoriteCount = m.Value.FavoriteCount;
                }
            });

            WeakReferenceMessenger.Default.Register<UnreadGeneralNotificationStatusMessage>(this, (r, m) =>
            {
                HasUnreadNotifications = m.Value;
            });
        }

        private async void StartListeners()
        {
            await LoadCategoriesAsync();
            StartListeningForNotifications();
            StartListeningForProducts();
        }

        private async Task LoadCategoriesAsync()
        {
            var result = await _productService.GetCategoriesAsync();
            if (result.Success && result.Data != null)
            {
                Categories.Clear();
                Categories.Add(new Category { CategoryId = null, Name = "Tümü", IconName = "apps.png" });
                foreach (var category in result.Data)
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

            var initialCheck = await _firebaseClient
                .Child(Constants.NotificationsCollection)
                .OrderBy("UserId")
                .EqualTo(currentUser.UserId)
                .OnceAsync<Notification>();

            if (initialCheck.Any(n => !n.Object.IsRead && n.Object.Type != NotificationType.NewMessage))
            {
                HasUnreadNotifications = true;
            }

            _notificationSubscription = _firebaseClient
                .Child(Constants.NotificationsCollection)
                .OrderBy("UserId")
                .EqualTo(currentUser.UserId)
                .AsObservable<Notification>()
                .Where(e => e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate)
                .Subscribe(entry =>
                {
                    if (entry.Object != null && !entry.Object.IsRead && entry.Object.Type != NotificationType.NewMessage)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            HasUnreadNotifications = true;
                        });
                    }
                });
        }

        private void StartListeningForProducts()
        {
            IsLoading = true;
            Products.Clear();

            _productSubscription = _firebaseClient
                .Child(Constants.ProductsCollection)
                .AsObservable<Product>()
                .Subscribe(e =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var product = e.Object;
                        product.ProductId = e.Key;

                        bool shouldBeInList = product.IsActive && !product.IsSold;
                        var existingProduct = Products.FirstOrDefault(p => p.ProductId == product.ProductId);

                        if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate)
                        {
                            if (shouldBeInList)
                            {
                                if (existingProduct != null)
                                {
                                    var index = Products.IndexOf(existingProduct);
                                    Products[index] = product;
                                }
                                else
                                {
                                    Products.Insert(0, product);
                                }
                            }
                            else if (existingProduct != null)
                            {
                                Products.Remove(existingProduct);
                            }
                        }
                        else if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.Delete)
                        {
                            if (existingProduct != null)
                            {
                                Products.Remove(existingProduct);
                            }
                        }

                        EmptyMessage = Products.Any() ? string.Empty : "Arama kriterlerinize uygun ürün bulunamadý";
                        IsLoading = false;
                    });
                });
        }

        [RelayCommand]
        private void SearchProducts()
        {
            // Real-time dinleyici aktif olduðu için, arama ve filtreleme lokalde yapýlabilir
            // veya daha verimli bir yaklaþým olarak Firebase sorgusunu yeniden oluþturabiliriz.
            // Þimdilik, dinleyiciyi yeniden baþlatarak basit bir çözüm uyguluyoruz.
            _productSubscription?.Dispose();
            StartListeningForProducts();
        }

        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
            SearchProducts();
        }

        [RelayCommand]
        private void ToggleFilterPanel()
        {
            ShowFilterPanel = !ShowFilterPanel;
        }

        [RelayCommand]
        private void ApplyFilters()
        {
            ShowFilterPanel = false;
            SearchProducts();
        }

        [RelayCommand]
        private void ClearFilters()
        {
            SelectedCategory = Categories.FirstOrDefault();
            SelectedType = null;
            SelectedSortOption = ProductSortOption.Newest;
            SearchText = string.Empty;
            SearchProducts();
        }

        [RelayCommand]
        private void CategoryTapped(Category category)
        {
            SelectedCategory = category;
            SearchProducts();
        }

        [RelayCommand]
        private async Task ProductTappedAsync(Product product)
        {
            // Eðer ürün null ise veya ürün satýlmýþsa, hiçbir þey yapma ve metottan çýk.
            if (product is null || product.IsSold)
                return;

            // Eðer ürün satýlmamýþsa, detay sayfasýna gitmeye devam et.
            await Shell.Current.GoToAsync($"{nameof(ProductDetailPage)}?ProductId={product.ProductId}");
        }

        [RelayCommand]
        private async Task GoToNotificationsAsync()
        {
            if (HasUnreadNotifications)
            {
                HasUnreadNotifications = false;
            }
            await Shell.Current.GoToAsync(nameof(NotificationsPage));
        }

        [RelayCommand]
        private async Task GoToAddProductAsync()
        {
            await Shell.Current.GoToAsync(nameof(AddProductPage));
        }

        partial void OnSelectedSortOptionChanged(ProductSortOption value)
        {
            SearchProducts();
        }

        partial void OnSelectedTypeChanged(ProductType? value)
        {
            SearchProducts();
        }

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

        [RelayCommand]
        private async Task GoToSurpriseBox()
        {
            await Shell.Current.GoToAsync(nameof(SurpriseBoxPage));
        }

        public void Dispose()
        {
            _notificationSubscription?.Dispose();
            _productSubscription?.Dispose();
            WeakReferenceMessenger.Default.Unregister<FavoriteCountChangedMessage>(this);
            WeakReferenceMessenger.Default.Unregister<UnreadGeneralNotificationStatusMessage>(this);
        }
    }
}