using CommunityToolkit.Mvvm.Messaging;
using KamPay.Models.Messages;
using ZXing.Net.Maui;

namespace KamPay.Views;

public partial class QRScannerPage : ContentPage
{
    public QRScannerPage()
    {
        InitializeComponent();
        barcodeReader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.All,
            AutoRotate = true,
            Multiple = false
        };
    }

    // HATA D�ZELTMES�: Metodun ad� XAML ile e�le�mesi i�in "BarcodesDetected" olarak de�i�tirildi.
    private void BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Tekrar tekrar taramay� �nlemek i�in kameray� durdur
            barcodeReader.IsDetecting = false;

            if (e.Results.Any())
            {
                string qrCodeData = e.Results[0].Value;

                // Taranan QR kod verisini i�eren bir mesaj G�NDER
                WeakReferenceMessenger.Default.Send(new QRCodeScannedMessage(qrCodeData));
            }

            // Bir �nceki sayfaya geri d�n
            await Shell.Current.GoToAsync("..");
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Sayfa a��ld���nda taramay� ba�lat
        barcodeReader.IsDetecting = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Sayfa kapand���nda taramay� durdur
        barcodeReader.IsDetecting = false;
    }
}