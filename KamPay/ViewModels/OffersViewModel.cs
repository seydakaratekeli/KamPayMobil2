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
        private readonly FirebaseClient _firebaseClient;

        // 🔥 YENİ: İlk yükleme kontrolü
        private bool _incomingInitialLoadComplete = false;
        private bool _outgoingInitialLoadComplete = false;

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
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);

            _ = InitializeAsync();
        }

        // 🔥 YENİ: Async initialization
        private async Task InitializeAsync()
        {
            IsLoading = true;

            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                IsLoading = false;
                return;
            }

            StartListeningForOffers(currentUser.UserId);
        }

        // 🔥 OPTIMIZE: Client-side filtering (Firebase.Database.net limitasyonu nedeniyle)
        private void StartListeningForOffers(string userId)
        {
            Console.WriteLine($"🔥 Real-time listener başlatılıyor: {userId}");

            IncomingOffers.Clear();
            OutgoingOffers.Clear();

            // 🔹 TÜM teklifleri dinle, client-side filtrele (Firebase.Database.net API limitasyonu)
            var allOffersSubscription = _firebaseClient
                .Child(Constants.TransactionsCollection)
                .AsObservable<Transaction>()
                .Buffer(TimeSpan.FromMilliseconds(200)) // 🔥 200ms buffer
                .Where(batch => batch.Any())
                .Subscribe(
                    events =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                // Her event'i kontrol et ve uygun listeye ekle
                                foreach (var e in events)
                                {
                                    if (e.Object == null) continue;

                                    var transaction = e.Object;
                                    transaction.TransactionId = e.Key;

                                    // Gelen teklif mi? (ben satıcıyım)
                                    if (transaction.SellerId == userId)
                                    {
                                        UpdateOfferInCollection(IncomingOffers, transaction, e.EventType);
                                    }
                                    // Giden teklif mi? (ben alıcıyım)
                                    else if (transaction.BuyerId == userId)
                                    {
                                        UpdateOfferInCollection(OutgoingOffers, transaction, e.EventType);
                                    }
                                }

                                // İlk yükleme tamamlandı
                                if (!_incomingInitialLoadComplete)
                                {
                                    _incomingInitialLoadComplete = true;
                                    CheckAndHideLoading();
                                }

                                if (!_outgoingInitialLoadComplete)
                                {
                                    _outgoingInitialLoadComplete = true;
                                    CheckAndHideLoading();
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Offer processing hatası: {ex.Message}");
                            }
                        });
                    },
                    error =>
                    {
                        Console.WriteLine($"❌ Firebase listener hatası: {error.Message}");
                        MainThread.BeginInvokeOnMainThread(() => IsLoading = false);
                    });

            // Her iki subscription'ı aynı observer'a bağla
            _incomingOffersSubscription = allOffersSubscription;
            _outgoingOffersSubscription = allOffersSubscription;
        }

        // 🔥 YENİ: Tek bir transaction'ı uygun listeye ekle/güncelle
        private void UpdateOfferInCollection(
            ObservableCollection<Transaction> collection,
            Transaction transaction,
            FirebaseEventType eventType)
        {
            var existing = collection.FirstOrDefault(t => t.TransactionId == transaction.TransactionId);

            switch (eventType)
            {
                case FirebaseEventType.InsertOrUpdate:
                    if (existing != null)
                    {
                        // Mevcut pozisyonda güncelle
                        var index = collection.IndexOf(existing);
                        collection[index] = transaction;
                    }
                    else
                    {
                        // Yeni teklif ekle (zaman sırasına göre)
                        InsertOfferSorted(collection, transaction);
                    }
                    break;

                case FirebaseEventType.Delete:
                    if (existing != null)
                        collection.Remove(existing);
                    break;
            }
        }

        // 🔥 YENİ: Her iki listener da tamamlandığında loading'i kapat
        private void CheckAndHideLoading()
        {
            if (_incomingInitialLoadComplete && _outgoingInitialLoadComplete)
            {
                IsLoading = false;
                Console.WriteLine("✅ Tüm teklifler yüklendi");
            }
        }



        // 🔥 YENİ: Teklifleri zaman sırasına göre ekle (en yeni üstte)
        private void InsertOfferSorted(ObservableCollection<Transaction> collection, Transaction newOffer)
        {
            if (collection.Count == 0)
            {
                collection.Add(newOffer);
                return;
            }

            // En yeni teklif en üstte olmalı
            if (collection[0].CreatedAt <= newOffer.CreatedAt)
            {
                collection.Insert(0, newOffer);
                return;
            }

            // Doğru pozisyonu bul
            for (int i = 0; i < collection.Count; i++)
            {
                if (collection[i].CreatedAt < newOffer.CreatedAt)
                {
                    collection.Insert(i, newOffer);
                    return;
                }
            }

            // En eskiyse en sona ekle
            collection.Add(newOffer);
        }

        // 🔥 OPTIMIZE: Refresh Command
        [RelayCommand]
        private async Task RefreshOffersAsync()
        {
            if (IsRefreshing) return;

            try
            {
                IsRefreshing = true;

                // Listener'ları durdur
                _incomingOffersSubscription?.Dispose();
                _outgoingOffersSubscription?.Dispose();

                // State'i sıfırla
                _incomingInitialLoadComplete = false;
                _outgoingInitialLoadComplete = false;
                IncomingOffers.Clear();
                OutgoingOffers.Clear();

                // Kullanıcıyı tekrar al
                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser != null)
                {
                    // Listener'ları yeniden başlat
                    StartListeningForOffers(currentUser.UserId);
                }

                await Task.Delay(300); // UI için kısa gecikme
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Refresh hatası: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert("Hata",
                    "Teklifler yenilenirken bir hata oluştu.", "Tamam");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        [RelayCommand]
        private void SelectIncoming()
        {
            IsIncomingSelected = true;
            IsOutgoingSelected = false;
        }

        [RelayCommand]
        private void SelectOutgoing()
        {
            IsIncomingSelected = false;
            IsOutgoingSelected = true;
        }

        [RelayCommand]
        private async Task ManageDeliveryAsync(Transaction transaction)
        {
            if (transaction == null) return;
            await Shell.Current.GoToAsync($"{nameof(QRCodeDisplayPage)}?transactionId={transaction.TransactionId}");
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

            try
            {
                var result = await _transactionService.RespondToOfferAsync(transaction.TransactionId, accept);

                if (result.Success)
                {
                    // Real-time listener otomatik güncelleyecek
                    var action = accept ? "kabul edildi" : "reddedildi";
                    await Application.Current.MainPage.DisplayAlert("Başarılı",
                        $"Teklif {action}.", "Tamam");
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata",
                    $"İşlem başarısız: {ex.Message}", "Tamam");
            }
        }

        [RelayCommand]
        private async Task CompletePaymentAsync(Transaction transaction)
        {
            if (transaction == null) return;

            if (transaction.Type != ProductType.Satis ||
                transaction.Status != TransactionStatus.Accepted ||
                transaction.PaymentStatus != PaymentStatus.Pending)
            {
                await Application.Current.MainPage.DisplayAlert("Bilgi",
                    "Bu işlem için ödeme yapılamaz.", "Tamam");
                return;
            }

            var confirm = await Application.Current.MainPage.DisplayAlert("Ödeme Simülasyonu",
                $"'{transaction.ProductTitle}' ürünü için ödemeyi tamamlamak üzeresiniz. Devam etmek istiyor musunuz?",
                "Evet, Tamamla", "Hayır");

            if (!confirm) return;

            IsLoading = true;

            try
            {
                var service = App.Current.Handler.MauiContext.Services.GetService<ITransactionService>();

                if (service is FirebaseTransactionService firebaseService)
                {
                    var currentUser = await _authService.GetCurrentUserAsync();
                    if (currentUser != null)
                    {
                        var result = await firebaseService.CompletePaymentAsync(transaction.TransactionId, currentUser.UserId);

                        if (result.Success)
                        {
                            await Application.Current.MainPage.DisplayAlert("Başarılı",
                                "Ödeme tamamlandı ve işlem sonuçlandırıldı.", "Tamam");
                        }
                        else
                        {
                            await Application.Current.MainPage.DisplayAlert("Hata",
                                result.Message ?? "Ödeme tamamlanamadı.", "Tamam");
                        }
                    }
                    else
                    {
                        await Application.Current.MainPage.DisplayAlert("Hata",
                            "Mevcut kullanıcı bulunamadı.", "Tamam");
                    }
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata",
                    $"Bir hata oluştu: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ConfirmDonationReceivedAsync(Transaction transaction)
        {
            if (transaction == null) return;

            if (transaction.Type != ProductType.Bagis ||
                transaction.Status != TransactionStatus.Accepted)
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
                    var result = await firebaseService.ConfirmDonationAsync(transaction.TransactionId, currentUser.UserId);

                    if (result.Success)
                    {
                        await Application.Current.MainPage.DisplayAlert("Başarılı",
                            "Bağış işlemi başarıyla tamamlandı.", "Tamam");
                    }
                    else
                    {
                        await Application.Current.MainPage.DisplayAlert("Hata",
                            result.Message ?? "İşlem tamamlanamadı.", "Tamam");
                    }
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata",
                    $"Bir hata oluştu: {ex.Message}", "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void Dispose()
        {
            Console.WriteLine("🧹 OffersViewModel dispose ediliyor...");
            _incomingOffersSubscription?.Dispose();
            _outgoingOffersSubscription?.Dispose();
            _incomingOffersSubscription = null;
            _outgoingOffersSubscription = null;
        }
    }
}