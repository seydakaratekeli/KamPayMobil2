using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Firebase.Database.Query;
using KamPay.Models;
using KamPay.Services;
using System.Linq;
using System.Threading.Tasks;

namespace KamPay.ViewModels
{
    [QueryProperty(nameof(TransactionId), "transactionId")]
    public partial class QRCodeViewModel : ObservableObject
    {
        private readonly IQRCodeService _qrCodeService;
        private readonly IAuthenticationService _authService;
        private readonly Firebase.Database.FirebaseClient _firebaseClient;

        [ObservableProperty]
        private string transactionId;

        // Kendi ürünümüzün teslimat bilgisi
        [ObservableProperty]
        private DeliveryQRCode? myDelivery;

        // Karşı tarafın ürününün teslimat bilgisi
        [ObservableProperty]
        private DeliveryQRCode? otherUserDelivery;

        [ObservableProperty]
        private Transaction? currentTransaction;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string pageTitle = "Teslimat Onayı";

        [ObservableProperty]
        private string instructionText = "Teslimatı başlatmak için QR kodunuzu diğer kullanıcıya okutun veya onun kodunu tarayın.";

        public QRCodeViewModel(IQRCodeService qrCodeService, IAuthenticationService authService)
        {
            _qrCodeService = qrCodeService;
            _authService = authService;
            _firebaseClient = new Firebase.Database.FirebaseClient(Helpers.Constants.FirebaseRealtimeDbUrl);
        }

        async partial void OnTransactionIdChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                await LoadTransactionAndQRCodesAsync();
            }
        }

        private async Task LoadTransactionAndQRCodesAsync()
        {
            IsLoading = true;
            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                IsLoading = false;
                await Application.Current.MainPage.DisplayAlert("Hata", "Kullanıcı bulunamadı.", "Tamam");
                return;
            }

            // 1. İşlem (Transaction) bilgisini çek
            CurrentTransaction = await _firebaseClient.Child("transactions").Child(TransactionId).OnceSingleAsync<Transaction>();
            if (CurrentTransaction == null)
            {
                IsLoading = false;
                await Application.Current.MainPage.DisplayAlert("Hata", "İşlem detayı bulunamadı.", "Tamam");
                return;
            }

            // 2. Bu işleme bağlı TÜM QR kodlarını çek
            var allCodes = (await _firebaseClient.Child("delivery_qrcodes").OnceAsync<DeliveryQRCode>())
                            .Select(c => c.Object);

            // 3. Mevcut kullanıcının ve karşı tarafın QR kodlarını ayır
            if (CurrentTransaction.SellerId == currentUser.UserId) // Eğer ben satıcıysam
            {
                MyDelivery = allCodes.FirstOrDefault(c => c.ProductId == CurrentTransaction.ProductId);
                OtherUserDelivery = allCodes.FirstOrDefault(c => c.ProductId == CurrentTransaction.OfferedProductId);
            }
            else // Eğer ben alıcıysam (teklifi yapan)
            {
                MyDelivery = allCodes.FirstOrDefault(c => c.ProductId == CurrentTransaction.OfferedProductId);
                OtherUserDelivery = allCodes.FirstOrDefault(c => c.ProductId == CurrentTransaction.ProductId);
            }

            UpdateUIState(); // Arayüzü duruma göre güncelle
            IsLoading = false;
        }

        [RelayCommand]
        private async Task ScanQRCodeAsync()
        {
            // QRScannerPage'e yönlendirme (bu kısım aynı kalabilir)
            await Shell.Current.GoToAsync("qrscanner");
        }

        public async Task ProcessScannedQRCode(string qrCodeData)
        {
            IsLoading = true;
            // Okunan kod, karşı tarafın kodumu diye kontrol et
            if (OtherUserDelivery == null || qrCodeData != OtherUserDelivery.QRCodeData)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Geçersiz veya bu takasa ait olmayan bir QR kod okuttunuz.", "Tamam");
                IsLoading = false;
                return;
            }

            // Zaten okutulmuş mu diye kontrol et
            if (OtherUserDelivery.IsUsed)
            {
                await Application.Current.MainPage.DisplayAlert("Bilgi", "Bu ürünün teslimatı zaten onaylanmış.", "Tamam");
                IsLoading = false;
                return;
            }

            // Teslimatı onayla
            var result = await _qrCodeService.CompleteDeliveryAsync(OtherUserDelivery.QRCodeId);
            if (result.Success)
            {
                await Application.Current.MainPage.DisplayAlert("Başarılı", $"'{OtherUserDelivery.ProductTitle}' ürününü teslim aldığınız onaylandı.", "Harika!");
                // Durumu yeniden yükle
                await LoadTransactionAndQRCodesAsync();
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
            }
            IsLoading = false;
        }

        private void UpdateUIState()
        {
            bool myDeliveryCompleted = MyDelivery?.IsUsed ?? false;
            bool otherDeliveryCompleted = OtherUserDelivery?.IsUsed ?? false;

            if (myDeliveryCompleted && otherDeliveryCompleted)
            {
                PageTitle = "Takas Tamamlandı!";
                InstructionText = "Her iki ürün de başarıyla teslim edildi. Bu ekranı kapatabilirsiniz.";
                // Otomatik olarak geri yönlendirme de yapılabilir.
                Task.Run(async () => {
                    await Task.Delay(3000);
                    await MainThread.InvokeOnMainThreadAsync(async () => await Shell.Current.GoToAsync(".."));
                });
            }
            else if (otherDeliveryCompleted)
            {
                PageTitle = "Şimdi Sıra Sizde";
                InstructionText = "Karşı tarafın ürününü teslim aldınız. Şimdi takası tamamlamak için kendi QR kodunuzu diğer kullanıcıya okutun.";
            }
            else if (myDeliveryCompleted)
            {
                PageTitle = "Onay Bekleniyor";
                InstructionText = "Kendi ürününüzü teslim ettiniz. Şimdi karşı tarafın ürününü teslim almak için onun QR kodunu okutun.";
            }
            else
            {
                PageTitle = "Teslimat Onayı";
                InstructionText = "Takası başlatmak için QR kodunuzu diğer kullanıcıya okutun veya onun kodunu tarayın.";
            }
        }
    }
}