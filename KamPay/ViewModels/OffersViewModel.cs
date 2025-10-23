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
        private readonly FirebaseClient _firebaseClient; // Bunu ekleyin
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
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl); // YENİ: Eklendi

            IncomingOffers = new ObservableCollection<Transaction>();
            OutgoingOffers = new ObservableCollection<Transaction>();
            IsIncomingSelected = true;

            StartListeningForOffers();

        }

        // OffersViewModel.cs içine yeni komut
        [RelayCommand]
        private async Task ConfirmDonationReceivedAsync(Transaction transaction)
        {
            if (transaction == null) return;

            // Hızlı kontrol
            if (transaction.Type != ProductType.Bagis || transaction.Status != TransactionStatus.Accepted)
            {
                return;
            }

            var confirm = await Application.Current.MainPage.DisplayAlert("Onay",
                $"'{transaction.ProductTitle}' ürününü teslim aldığınızı onaylıyor musunuz?",
                "Evet, Teslim Aldım", "Hayır");

            if (!confirm) return;

            IsLoading = true;
            try
            {
                var service = App.Current.Handler.MauiContext.Services.GetService<ITransactionService>();
                var currentUser = await _authService.GetCurrentUserAsync();

                if (service is FirebaseTransactionService firebaseService && currentUser != null)
                {
                    // Yeni servis metodunu çağırıyoruz
                    var result = await firebaseService.ConfirmDonationAsync(transaction.TransactionId, currentUser.UserId);

                    if (result.Success)
                    {
                        await Application.Current.MainPage.DisplayAlert("Başarılı", "Bağış işlemi başarıyla tamamlandı.", "Tamam");
                    }
                    else
                    {
                        await Application.Current.MainPage.DisplayAlert("Hata", result.Message ?? "İşlem tamamlanamadı.", "Tamam");
                    }
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Bir hata oluştu: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }
        [RelayCommand]
        private async Task CompletePaymentAsync(Transaction transaction)
        {
            if (transaction == null) return;

            // Converter zaten kontrol ediyor ama burada da bir güvenlik kontrolü yapalım
            if (transaction.Type != ProductType.Satis ||
                transaction.Status != TransactionStatus.Accepted ||
                transaction.PaymentStatus != PaymentStatus.Pending)
            {
                await Application.Current.MainPage.DisplayAlert("Bilgi", "Bu işlem için ödeme yapılamaz.", "Tamam");
                return;
            }

            var confirm = await Application.Current.MainPage.DisplayAlert("Ödeme Simülasyonu",
                $"'{transaction.ProductTitle}' ürünü için ödemeyi tamamlamak üzeresiniz (Bu gerçek bir ödeme değildir). Devam etmek istiyor musunuz?",
                "Evet, Tamamla", "Hayır");

            if (!confirm) return;

            IsLoading = true;

            try
            {
                // Not: _transactionService'i direkt kullanmak yerine,
                // Bağımlılığın doğru enjekte edildiğinden emin olmak için App'ten de alabiliriz,
                // ancak constructor'da zaten enjekte edilmiş.
                // Eğer _transactionService'in tipi FirebaseTransactionService değilse
                // (ki ITransactionService olarak enjekte ediliyor),
                // en güvenli yol servisi DI'dan tekrar çözmektir.

                var service = App.Current.Handler.MauiContext.Services.GetService<ITransactionService>();

                if (service is FirebaseTransactionService firebaseService)
                {
                    var currentUser = await _authService.GetCurrentUserAsync();
                    if (currentUser != null)
                    {
                        var result = await firebaseService.CompletePaymentAsync(transaction.TransactionId, currentUser.UserId);

                        if (result.Success)
                        {
                            await Application.Current.MainPage.DisplayAlert("Başarılı", "Ödeme tamamlandı ve işlem sonuçlandırıldı.", "Tamam");
                            // Real-time listener (StartListeningForOffers) değişikliği otomatik olarak yakalamalı.
                        }
                        else
                        {
                            await Application.Current.MainPage.DisplayAlert("Hata", result.Message ?? "Ödeme tamamlanamadı.", "Tamam");
                        }
                    }
                    else
                    {
                        await Application.Current.MainPage.DisplayAlert("Hata", "Mevcut kullanıcı bulunamadı.", "Tamam");
                    }
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Ödeme servisi (FirebaseTransactionService) bulunamadı.", "Tamam");
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", $"Bir hata oluştu: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
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
