using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using KamPay.Views;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using System.Reactive.Linq;

namespace KamPay.ViewModels
{
    // Favoriler ViewModel
    public partial class FavoritesViewModel : ObservableObject, IDisposable
    {
        private readonly IFavoriteService _favoriteService;
        private readonly IAuthenticationService _authService;
        private IDisposable _favoritesSubscription;
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);

        [ObservableProperty]
        private bool isLoading = true;

        [ObservableProperty]
        private string emptyMessage = "Henüz favori ürününüz yok";

        public ObservableCollection<Favorite> FavoriteItems { get; } = new();

        public FavoritesViewModel(IFavoriteService favoriteService, IProductService productService, IAuthenticationService authService)
        {
            _favoriteService = favoriteService;
            _authService = authService;
            StartListeningForFavorites();
        }

        private async void StartListeningForFavorites()
        {
            IsLoading = true;
            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                IsLoading = false;
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
                        IsLoading = false;
                    });
                });
        }
        [RelayCommand]
        private async Task ProductTappedAsync(Favorite favorite)
        {
            if (favorite == null) return;
            await Shell.Current.GoToAsync($"ProductDetailPage?productId={favorite.ProductId}");
        }

        [RelayCommand]
        private async Task RemoveFavoriteAsync(Favorite favorite)
        {
            if (favorite == null) return;
            var currentUser = await _authService.GetCurrentUserAsync();
            await _favoriteService.RemoveFromFavoritesAsync(currentUser.UserId, favorite.ProductId);
            // Anlýk dinleyici sayesinde liste otomatik güncellenecek
        }

        public void Dispose()
        {
            _favoritesSubscription?.Dispose();
        }

    }
}