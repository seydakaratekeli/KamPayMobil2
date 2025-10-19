// KamPay/ViewModels/FavoritesViewModel.cs

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using System.Reactive.Linq;
using KamPay.Views;

namespace KamPay.ViewModels
{
    public partial class FavoritesViewModel : ObservableObject, IDisposable
    {
        private readonly IFavoriteService _favoriteService;
        private readonly IAuthenticationService _authService;
        private IDisposable _favoritesSubscription;
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);
        private bool _isInitialized = false; // Verinin tekrar tekrar yüklenmesini engellemek için

        [ObservableProperty]
        private bool isLoading = true;

        [ObservableProperty]
        private string emptyMessage = "Henüz favori ürününüz yok";

        public ObservableCollection<Favorite> FavoriteItems { get; } = new();

        public FavoritesViewModel(IFavoriteService favoriteService, IProductService productService, IAuthenticationService authService)
        {
            _favoriteService = favoriteService;
            _authService = authService;
            // Constructor'daki bu çaðrýyý SÝLÝYORUZ:
            // StartListeningForFavorites();
        }

        // YENÝ: Baþlatma komutu
        [RelayCommand]
        private async Task InitializeAsync()
        {
            if (_isInitialized) return; // Zaten baþlatýldýysa tekrar çalýþtýrma

            IsLoading = true;
            try
            {
                await StartListeningForFavoritesAsync();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                // Hata durumunu kullanýcýya bildirmek için ideal bir yer
                Console.WriteLine($"Favoriler yüklenirken hata oluþtu: {ex.Message}");
                EmptyMessage = "Favoriler yüklenemedi.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task StartListeningForFavoritesAsync()
        {
            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                EmptyMessage = "Favorileri görmek için giriþ yapmalýsýnýz.";
                return;
            }

            FavoriteItems.Clear();

            _favoritesSubscription = _firebaseClient
                .Child(Constants.FavoritesCollection)
                .OrderBy("UserId")
                .EqualTo(currentUser.UserId)
                .AsObservable<Favorite>()
                .Subscribe(e =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var favorite = e.Object;
                        favorite.FavoriteId = e.Key;

                        var existingFav = FavoriteItems.FirstOrDefault(f => f.FavoriteId == favorite.FavoriteId);

                        if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate)
                        {
                            if (existingFav != null)
                            {
                                var index = FavoriteItems.IndexOf(existingFav);
                                FavoriteItems[index] = favorite;
                            }
                            else
                            {
                                FavoriteItems.Insert(0, favorite);
                            }
                        }
                        else if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.Delete)
                        {
                            if (existingFav != null)
                            {
                                FavoriteItems.Remove(existingFav);
                            }
                        }
                        EmptyMessage = FavoriteItems.Any() ? string.Empty : "Henüz favori ürününüz yok.";
                    });
                });
        }

        [RelayCommand]
        private async Task ProductTappedAsync(Favorite favorite)
        {
            if (favorite == null) return;
            await Shell.Current.GoToAsync($"{nameof(ProductDetailPage)}?productId={favorite.ProductId}");
        }

        [RelayCommand]
        private async Task RemoveFavoriteAsync(Favorite favorite)
        {
            if (favorite == null) return;
            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser != null)
            {
                await _favoriteService.RemoveFromFavoritesAsync(currentUser.UserId, favorite.ProductId);
            }
        }

        public void Dispose()
        {
            _favoritesSubscription?.Dispose();
            _isInitialized = false; // Sayfadan çýkýldýðýnda yeniden yüklenebilmesi için sýfýrla
        }
    }
}