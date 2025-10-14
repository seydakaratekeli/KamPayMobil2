using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;
using ZXing.Net.Maui;
using ZXing;

namespace KamPay.ViewModels
{
    public partial class QRCodeViewModel : ObservableObject
    {
        private readonly IQRCodeService _qrCodeService;
        private readonly IProductService _productService;
        private readonly IAuthenticationService _authService;

        [ObservableProperty]
        private DeliveryQRCode deliveryQRCode;

        [ObservableProperty]
        private string qrCodeImageSource;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private bool isScanning;

        public QRCodeViewModel(
            IQRCodeService qrCodeService,
            IProductService productService,
            IAuthenticationService authService)
        {
            _qrCodeService = qrCodeService;
            _productService = productService;
            _authService = authService;
        }

        // ✅ Tek parametreli versiyon — MVVMTK0007 hatası artık olmaz
        [RelayCommand]
        private async Task GenerateQRCodeAsync(string productId)
        {
            try
            {
                IsLoading = true;

                var currentUser = await _authService.GetCurrentUserAsync();

                // Alıcı ID'sini gerekirse sabit belirle veya başka yerden al
                var buyerId = currentUser.UserId;

                var result = await _qrCodeService.GenerateDeliveryQRCodeAsync(
                    productId,
                    currentUser.UserId,
                    buyerId
                );

                if (result.Success)
                {
                    DeliveryQRCode = result.Data;
                    QrCodeImageSource = DeliveryQRCode.QRCodeData;

                    await Application.Current.MainPage.DisplayAlert(
                        "Başarılı",
                        "QR kod oluşturuldu! Alıcıya gösterin.",
                        "Tamam"
                    );
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
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
        private async Task ScanQRCodeAsync()
        {
            try
            {
                IsScanning = true;

                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                    status = await Permissions.RequestAsync<Permissions.Camera>();

                if (status != PermissionStatus.Granted)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "İzin Gerekli",
                        "QR kod okutmak için kamera iznine ihtiyaç var",
                        "Tamam"
                    );
                    return;
                }

                await Shell.Current.GoToAsync("qrscanner");
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
            finally
            {
                IsScanning = false;
            }
        }

        public async Task ProcessScannedQRCode(string qrCodeData)
        {
            try
            {
                IsLoading = true;

                var result = await _qrCodeService.ValidateQRCodeAsync(qrCodeData);

                if (result.Success)
                {
                    var confirm = await Application.Current.MainPage.DisplayAlert(
                        "Teslimat Onayı",
                        $"Ürün: {result.Data.ProductId}\n\nTeslimatı onaylıyor musunuz?",
                        "Evet",
                        "Hayır"
                    );

                    if (confirm)
                    {
                        var completeResult = await _qrCodeService.CompleteDeliveryAsync(result.Data.QRCodeId);

                        if (completeResult.Success)
                        {
                            await Application.Current.MainPage.DisplayAlert(
                                "Başarılı",
                                "Teslimat tamamlandı! 🎉",
                                "Tamam"
                            );

                            await Shell.Current.GoToAsync("..");
                        }
                    }
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
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
    }
}
