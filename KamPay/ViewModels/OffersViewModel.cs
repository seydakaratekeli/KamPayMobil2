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
using System.Diagnostics; // Stopwatch için

namespace KamPay.ViewModels
{
    public partial class OffersViewModel : ObservableObject, IDisposable
    {
        private readonly ITransactionService _transactionService;
        private readonly IAuthenticationService _authService;
        private IDisposable _allOffersSubscription;
        private readonly FirebaseClient _firebaseClient;

        // 🔥 CACHE: Duplicate prevention
        private readonly HashSet<string> _incomingIds = new();
        private readonly HashSet<string> _outgoingIds = new();

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

        // 🔥 OPTİMİZE: Tek subscription, batch processing
        private void StartListeningForOffers(string userId)
        {
            Console.WriteLine($"🔥 Offers listener başlatılıyor: {userId}");

            IncomingOffers.Clear();
            OutgoingOffers.Clear();
            _incomingIds.Clear();
            _outgoingIds.Clear();

            _allOffersSubscription = _firebaseClient
                .Child(Constants.TransactionsCollection)
                .AsObservable<Transaction>()
                .Where(e => e.Object != null)
                .Buffer(TimeSpan.FromMilliseconds(300)) // 🔥 300ms batch
                .Where(batch => batch.Any())
                .Subscribe(
                    events =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                ProcessOfferBatch(events, userId);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Offer batch hatası: {ex.Message}");
                            }
                            finally
                            {
                                CheckAndHideLoading();
                            }
                        });
                    },
                    error =>
                    {
                        Console.WriteLine($"❌ Firebase listener hatası: {error.Message}");
                        MainThread.BeginInvokeOnMainThread(() => IsLoading = false);
                    });
        }

        // 🔥 OPTİMİZE: Smart batch processing
        private void ProcessOfferBatch(IList<FirebaseEvent<Transaction>> events, string userId)
        {
            bool hasIncomingChanges = false;
            bool hasOutgoingChanges = false;

            foreach (var e in events)
            {
                if (e.Object == null) continue;

                var transaction = e.Object;
                transaction.TransactionId = e.Key;

                // Gelen teklif mi? (ben satıcıyım)
                if (transaction.SellerId == userId)
                {
                    if (UpdateOfferInCollection(IncomingOffers, _incomingIds, transaction, e.EventType))
                    {
                        hasIncomingChanges = true;
                    }
                }
                // Giden teklif mi? (ben alıcıyım)
                else if (transaction.BuyerId == userId)
                {
                    if (UpdateOfferInCollection(OutgoingOffers, _outgoingIds, transaction, e.EventType))
                    {
                        hasOutgoingChanges = true;
                    }
                }
            }

            // 🔥 Sadece değişenler için sıralama
            if (hasIncomingChanges)
            {
                SortOffersInPlace(IncomingOffers);
                _incomingInitialLoadComplete = true;
            }

            if (hasOutgoingChanges)
            {
                SortOffersInPlace(OutgoingOffers);
                _outgoingInitialLoadComplete = true;
            }
        }

        // 🔥 YENİ: Tek bir transaction'ı güncelle (duplicate check)
        private bool UpdateOfferInCollection(
            ObservableCollection<Transaction> collection,
            HashSet<string> idTracker,
            Transaction transaction,
            FirebaseEventType eventType)
        {
            var existing = collection.FirstOrDefault(t => t.TransactionId == transaction.TransactionId);

            switch (eventType)
            {
                case FirebaseEventType.InsertOrUpdate:
                    if (existing != null)
                    {
                        // Güncelleme
                        var index = collection.IndexOf(existing);
                        collection[index] = transaction;
                        return true;
                    }
                    else
                    {
                        // 🔥 Duplicate check
                        if (!idTracker.Contains(transaction.TransactionId))
                        {
                            collection.Add(transaction);
                            idTracker.Add(transaction.TransactionId);
                            return true;
                        }
                    }
                    break;

                case FirebaseEventType.Delete:
                    if (existing != null)
                    {
                        collection.Remove(existing);
                        idTracker.Remove(transaction.TransactionId);
                        return true;
                    }
                    break;
            }

            return false;
        }

        // 🔥 YENİ: In-place sorting
        private void SortOffersInPlace(ObservableCollection<Transaction> collection)
        {
            var sorted = collection.OrderByDescending(t => t.CreatedAt).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var currentIndex = collection.IndexOf(sorted[i]);
                if (currentIndex != i && currentIndex >= 0)
                {
                    collection.Move(currentIndex, i);
                }
            }
        }

        private void CheckAndHideLoading()
        {
            if (_incomingInitialLoadComplete && _outgoingInitialLoadComplete)
            {
                IsLoading = false;
                Console.WriteLine("✅ Tüm teklifler yüklendi");
            }
        }

        // 🔥 OPTİMİZE: Refresh Command
        [RelayCommand]
        private async Task RefreshOffersAsync()
        {
            if (IsRefreshing) return;

            try
            {
                IsRefreshing = true;

                // Listener'ı durdur
                _allOffersSubscription?.Dispose();

                // State'i sıfırla
                _incomingInitialLoadComplete = false;
                _outgoingInitialLoadComplete = false;
                _incomingIds.Clear();
                _outgoingIds.Clear();
                IncomingOffers.Clear();
                OutgoingOffers.Clear();

                // Kullanıcıyı tekrar al
                var currentUser = await _authService.GetCurrentUserAsync();
                if (currentUser != null)
                {
                    StartListeningForOffers(currentUser.UserId);
                }

                await Task.Delay(300);
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
            _allOffersSubscription?.Dispose();
            _allOffersSubscription = null;
            _incomingIds.Clear();
            _outgoingIds.Clear();
        }
    }
}