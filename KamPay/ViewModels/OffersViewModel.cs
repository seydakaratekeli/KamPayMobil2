using System.Collections.ObjectModel;
using System.Diagnostics; // Hata ayýklama için EKLENDÝ
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;

namespace KamPay.ViewModels
{
    public partial class OffersViewModel : ObservableObject
    {
        private readonly ITransactionService _transactionService;
        private readonly IAuthenticationService _authService;

        public ObservableCollection<Transaction> IncomingOffers { get; } = new();
        public ObservableCollection<Transaction> OutgoingOffers { get; } = new();

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private bool isIncomingSelected = true;

        [ObservableProperty]
        private bool isOutgoingSelected = false;

        public OffersViewModel(ITransactionService transactionService, IAuthenticationService authService)
        {
            _transactionService = transactionService;
            _authService = authService;
            // Sayfa açýldýðýnda otomatik yükleme için bu satýr önemli
            LoadOffersAsync();
        }

        [RelayCommand]
        private async Task LoadOffersAsync()
        {
            if (IsLoading) return;

            IsLoading = true;
            Debug.WriteLine("[OffersViewModel] Teklifler yükleniyor...");

            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                Debug.WriteLine("[OffersViewModel HATA] Aktif kullanýcý bulunamadý! Teklifler yüklenemedi.");
                IsLoading = false;
                return;
            }

            Debug.WriteLine($"[OffersViewModel] Aktif kullanýcý ID: {currentUser.UserId}");

            // Gelen Teklifler
            var incomingResult = await _transactionService.GetIncomingOffersAsync(currentUser.UserId);
            if (incomingResult.Success && incomingResult.Data != null)
            {
                Debug.WriteLine($"[OffersViewModel] {incomingResult.Data.Count} adet gelen teklif bulundu.");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IncomingOffers.Clear();
                    foreach (var offer in incomingResult.Data) IncomingOffers.Add(offer);
                });
            }
            else
            {
                Debug.WriteLine($"[OffersViewModel HATA] Gelen teklifler alýnamadý: ");
            }

            // Giden Teklifler
            var outgoingResult = await _transactionService.GetMyOffersAsync(currentUser.UserId);
            if (outgoingResult.Success && outgoingResult.Data != null)
            {
                Debug.WriteLine($"[OffersViewModel] {outgoingResult.Data.Count} adet giden teklif bulundu.");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OutgoingOffers.Clear();
                    foreach (var offer in outgoingResult.Data) OutgoingOffers.Add(offer);
                });
            }
            else
            {
                Debug.WriteLine($"[OffersViewModel HATA] Giden teklifler alýnamadý:");
            }

            IsLoading = false;
            Debug.WriteLine("[OffersViewModel] Teklif yükleme tamamlandý.");
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
                }
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
            }
        }
    }
}