using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Firebase.Database;
using Firebase.Database.Query;
using Firebase.Database.Streaming;
using KamPay.Helpers;
using KamPay.Models;
using KamPay.Services;
using KamPay.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace KamPay.ViewModels
{
    public partial class OffersViewModel : ObservableObject, IDisposable
    {
        private readonly ITransactionService _transactionService;
        private readonly IAuthenticationService _authService;
        private IDisposable _incomingOffersSubscription;
        private IDisposable _outgoingOffersSubscription;
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);

        public ObservableCollection<Transaction> IncomingOffers { get; } = new();
        public ObservableCollection<Transaction> OutgoingOffers { get; } = new();

        [ObservableProperty]
        private bool isLoading = true;

        [ObservableProperty]
        private bool isRefreshing;

        [ObservableProperty]
        private bool isIncomingSelected = true;

        [ObservableProperty]
        private bool isOutgoingSelected = false;

        public OffersViewModel(ITransactionService transactionService, IAuthenticationService authService)
        {
            _transactionService = transactionService;
            _authService = authService;
            StartListeningForOffers();
        }

        private async void StartListeningForOffers()
        {
            IsLoading = true;

            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                IsLoading = false;
                return;
            }

            IncomingOffers.Clear();
            OutgoingOffers.Clear();

            // 🔹 Gelen teklifler
            _incomingOffersSubscription = _firebaseClient
                .Child(Constants.TransactionsCollection)
                .OrderBy("SellerId")
                .EqualTo(currentUser.UserId)
                .AsObservable<Transaction>()
               .Subscribe(e => {
                   UpdateCollection(IncomingOffers, e);
                   IsLoading = false; // <-- İlk veri geldiğinde animasyonu gizle
               });

            // 🔹 Giden teklifler
            _outgoingOffersSubscription = _firebaseClient
                .Child(Constants.TransactionsCollection)
                .OrderBy("BuyerId")
                .EqualTo(currentUser.UserId)
                .AsObservable<Transaction>()
.Subscribe(e => {
    UpdateCollection(OutgoingOffers, e);
    IsLoading = false; // <-- İlk veri geldiğinde animasyonu gizle
});

        }

        private void UpdateCollection(ObservableCollection<Transaction> collection, FirebaseEvent<Transaction> e)
        {
            if (e.Object == null) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var transaction = e.Object;
                transaction.TransactionId = e.Key;

                var existing = collection.FirstOrDefault(t => t.TransactionId == transaction.TransactionId);

                switch (e.EventType)
                {
                    case FirebaseEventType.InsertOrUpdate:
                        if (existing != null)
                        {
                            var index = collection.IndexOf(existing);
                            collection[index] = transaction;
                        }
                        else
                        {
                            collection.Insert(0, transaction);
                        }
                        break;

                    case FirebaseEventType.Delete:
                        if (existing != null)
                            collection.Remove(existing);
                        break;
                }
            });
        }

      
        [RelayCommand]
        private async Task RefreshOffersAsync()
        {
            IsRefreshing = true;
            // Dinleyicileri durdurup yeniden başlatarak verileri tazeleyelim
            Dispose();
            StartListeningForOffers();
            await Task.Delay(500); // UI'ın güncellenmesi için küçük bir gecikme
            IsRefreshing = false;
        }

        [RelayCommand]
        private void SelectIncoming()
        {
            IsIncomingSelected = true;
            IsOutgoingSelected = false;
        }

        [RelayCommand]
        private async Task ManageDeliveryAsync(Transaction transaction)
        {
            if (transaction == null) return;
            await Shell.Current.GoToAsync($"{nameof(QRCodeDisplayPage)}?transactionId={transaction.TransactionId}");
        }

        [RelayCommand]
        private void SelectOutgoing()
        {
            IsIncomingSelected = false;
            IsOutgoingSelected = true;
        }

        [RelayCommand]
        private async Task AcceptOfferAsync(Transaction transaction)
        {
            await RespondToOfferInternalAsync(transaction, true);
        }

        [RelayCommand]
        private async Task RejectOfferAsync(Transaction transaction)
        {
            await RespondToOfferInternalAsync(transaction, false);
        }

        private async Task RespondToOfferInternalAsync(Transaction transaction, bool accept)
        {
            if (transaction == null) return;

            var result = await _transactionService.RespondToOfferAsync(transaction.TransactionId, accept);
            if (result.Success)
            {
                var offerInList = IncomingOffers.FirstOrDefault(o => o.TransactionId == transaction.TransactionId);
                if (offerInList != null)
                {
                    offerInList.Status = result.Data.Status;
                    OnPropertyChanged(nameof(IncomingOffers));
                }
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
            }
        }

        public void Dispose()
        {
            _incomingOffersSubscription?.Dispose();
            _outgoingOffersSubscription?.Dispose();
        }
    }
}
