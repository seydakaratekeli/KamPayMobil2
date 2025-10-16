using KamPay.ViewModels;

namespace KamPay.Views;

public partial class QRScannerPage : ContentPage
{
    private readonly QRCodeViewModel _viewModel;

    public QRScannerPage(QRCodeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        // ViewModel'i doðrudan atamýyoruz çünkü bu sayfanýn kendi BindingContext'i olmayacak,
        // sadece viewModel'deki bir metodu çaðýracak.
    }

    private void BarcodesDetected(object sender, ZXing.Net.Maui.BarcodeDetectionEventArgs e)
    {
        if (e.Results?.Any() ?? false)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Taramayý durdur
                barcodeReader.IsDetecting = false;

                // ViewModel'deki metodu çaðýr ve sonucu iþle
                await _viewModel.ProcessScannedQRCode(e.Results[0].Value);
            });
        }
    }
}