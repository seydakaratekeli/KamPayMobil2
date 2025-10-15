using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.Models;
using KamPay.Services;
using KamPay.Views;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

using CommunityToolkit.Maui.Core; 

namespace KamPay.ViewModels
{
    public class ShowTradeOfferPopupMessage
    {
        public Product TargetProduct { get; }
        public ShowTradeOfferPopupMessage(Product targetProduct)
        {
            TargetProduct = targetProduct;
        }
    }
    [QueryProperty(nameof(ProductId), "productId")]
    public partial class ProductDetailViewModel : ObservableObject
    {
        // Gerekli tüm servisleri tanýmlýyoruz
        private readonly IProductService _productService;
        private readonly IAuthenticationService _authService;
        private readonly IFavoriteService _favoriteService;
        private readonly IMessagingService _messagingService;
        private readonly ITransactionService _transactionService;

        [ObservableProperty]
        private string productId;

        [ObservableProperty]
        private Product product;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private bool isOwner;

        [ObservableProperty]
        private bool canContact;

        [ObservableProperty]
        private int currentImageIndex;

        [ObservableProperty]
        private bool isFavorite;

        public ObservableCollection<string> ProductImages { get; } = new();

        public ProductDetailViewModel(
            IProductService productService,
            IAuthenticationService authService,
            IFavoriteService favoriteService,
            IMessagingService messagingService,
            ITransactionService transactionService)
        {
            _productService = productService;
            _authService = authService;
            _favoriteService = favoriteService;
            _messagingService = messagingService;
            _transactionService = transactionService;
        }

        partial void OnProductIdChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _ = LoadProductAsync();
            }
        }

        [RelayCommand]
        private async Task LoadProductAsync()
        {
            try
            {
                IsLoading = true;

                var result = await _productService.GetProductByIdAsync(ProductId);

                if (result.Success && result.Data != null)
                {
                    Product = result.Data;

                    ProductImages.Clear();
                    if (Product.ImageUrls != null && Product.ImageUrls.Any())
                    {
                        foreach (var imageUrl in Product.ImageUrls)
                        {
                            ProductImages.Add(imageUrl);
                        }
                    }

                    var currentUser = await _authService.GetCurrentUserAsync();
                    if (currentUser != null)
                    {
                        IsOwner = Product.UserId == currentUser.UserId;
                        CanContact = !IsOwner && Product.IsActive && !Product.IsSold;

                        var favResult = await _favoriteService.IsFavoriteAsync(currentUser.UserId, ProductId);
                        IsFavorite = favResult.Success && favResult.Data;
                    }
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Ürün bulunamadý", "Tamam");
                    await Shell.Current.GoToAsync("..");
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Ürün yüklenirken hata oluþtu: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ContactSellerAsync()
        {
            if (Product == null || IsLoading) return;

            try
            {
                IsLoading = true;
                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null || currentUser.UserId == Product.UserId) return;

                var conversationResult = await _messagingService.GetOrCreateConversationAsync(currentUser.UserId, Product.UserId, Product.ProductId);

                if (conversationResult.Success)
                {
                    await Shell.Current.GoToAsync($"{nameof(ChatPage)}?conversationId={conversationResult.Data.ConversationId}");
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", conversationResult.Message, "Tamam");
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Ýletiþim kurulamadý: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }


        [RelayCommand]
        private async Task SendRequestAsync()
        {
            if (Product == null || IsLoading) return;
            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Bu iþlem için giriþ yapmalýsýnýz.", "Tamam");
                return;
            }

            IsLoading = true;
            try
            {
                switch (Product.Type)
                {
                    case ProductType.Takas:
                        
                        WeakReferenceMessenger.Default.Send(new ShowTradeOfferPopupMessage(Product));
                        break;

                    case ProductType.Satis:
                    case ProductType.Bagis:
                        var result = await _transactionService.CreateRequestAsync(Product, currentUser);
                        await Application.Current.MainPage.DisplayAlert(result.Success ? "Baþarýlý" : "Hata", result.Message, "Tamam");
                        break;
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ToggleFavoriteAsync()
        {
            if (isLoading || isOwner || Product == null) return;

            try
            {
                IsLoading = true;
                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser == null) return;

                if (IsFavorite)
                {
                    var result = await _favoriteService.RemoveFromFavoritesAsync(currentUser.UserId, ProductId);
                    if (result.Success)
                    {
                        IsFavorite = false;
                        Product.FavoriteCount--;
                    }
                }
                else
                {
                    var result = await _favoriteService.AddToFavoritesAsync(currentUser.UserId, ProductId);
                    if (result.Success)
                    {
                        IsFavorite = true;
                        Product.FavoriteCount++;
                    }
                }
                WeakReferenceMessenger.Default.Send(new FavoriteCountChangedMessage(Product));
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ShareProductAsync()
        {
            if (Product == null) return;
            try
            {
                await Share.RequestAsync(new ShareTextRequest
                {
                    Title = Product.Title,
                    Text = $"{Product.Title}\n{Product.Description}\n{Product.PriceText}\n\nKamPay ile paylaþýldý"
                });
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Paylaþýlamadý: {ex.Message}", "Tamam");
            }
        }

        [RelayCommand]
        private async Task MarkAsSoldAsync()
        {
            if (Product == null) return;

            var confirm = await Application.Current.MainPage.DisplayAlert(
                "Onay",
                "Ürünü satýldý olarak iþaretlemek istediðinize emin misiniz?",
                "Evet",
                "Hayýr"
            );

            if (!confirm) return;

            try
            {
                IsLoading = true;

                var result = await _productService.MarkAsSoldAsync(ProductId);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Baþarýlý",
                        "Ürün satýldý olarak iþaretlendi",
                        "Tamam"
                    );

                    await LoadProductAsync();
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Ýþlem baþarýsýz: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task EditProductAsync()
        {
            if (Product == null) return;
            await Shell.Current.GoToAsync($"{nameof(EditProductPage)}?productId={ProductId}");
        }

        [RelayCommand]
        private async Task DeleteProductAsync()
        {
            if (Product == null) return;

            var confirm = await Application.Current.MainPage.DisplayAlert(
                "Onay",
                "Ürünü silmek istediðinize emin misiniz? Bu iþlem geri alýnamaz.",
                "Evet, Sil",
                "Ýptal"
            );

            if (!confirm) return;

            try
            {
                IsLoading = true;

                var result = await _productService.DeleteProductAsync(ProductId);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Baþarýlý", "Ürün silindi", "Tamam");
                    await Shell.Current.GoToAsync("..");
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Silme baþarýsýz: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ReportProductAsync()
        {
            if (Product == null) return;

            var reason = await Application.Current.MainPage.DisplayActionSheet(
                "Þikayet Nedeni",
                "Ýptal",
                null,
                "Uygunsuz içerik",
                "Sahte ürün",
                "Yanýltýcý bilgi",
                "Diðer"
            );

            if (reason != null && reason != "Ýptal")
            {
                await Application.Current.MainPage.DisplayAlert("Bilgi", "Þikayetiniz alýndý. Ýnceleme süreci baþlatýldý.", "Tamam");
            }
        }

        [RelayCommand]
        private void PreviousImage()
        {
            if (ProductImages.Count == 0) return;

            CurrentImageIndex--;
            if (CurrentImageIndex < 0)
            {
                CurrentImageIndex = ProductImages.Count - 1;
            }
        }

        [RelayCommand]
        private void NextImage()
        {
            if (ProductImages.Count == 0) return;

            CurrentImageIndex++;
            if (CurrentImageIndex >= ProductImages.Count)
            {
                CurrentImageIndex = 0;
            }
        }

        [RelayCommand]
        private async Task OpenLocationAsync()
        {
            if (Product == null || Product.Latitude == null || Product.Longitude == null) return;

            try
            {
                var location = new Location(Product.Latitude.Value, Product.Longitude.Value);
                var options = new MapLaunchOptions { Name = Product.Location };

                await Map.OpenAsync(location, options);
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Harita açýlamadý: {ex.Message}", "Tamam");
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    public class FavoriteCountChangedMessage : CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<Product>
    {
        public FavoriteCountChangedMessage(Product value) : base(value) { }
    }
}