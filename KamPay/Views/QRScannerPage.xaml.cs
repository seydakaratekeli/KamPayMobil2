using KamPay.ViewModels;

namespace KamPay.Views;

public partial class QRScannerPage : ContentPage
{
    private readonly QRCodeViewModel _viewModel;

    public QRScannerPage(QRCodeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        // ViewModel'i do�rudan atam�yoruz ��nk� bu sayfan�n kendi BindingContext'i olmayacak,
        // sadece viewModel'deki bir metodu �a��racak.
    }

    private void BarcodesDetected(object sender, ZXing.Net.Maui.BarcodeDetectionEventArgs e)
    {
        if (e.Results?.Any() ?? false)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Taramay� durdur
                barcodeReader.IsDetecting = false;

                // ViewModel'deki metodu �a��r ve sonucu i�le
                await _viewModel.ProcessScannedQRCode(e.Results[0].Value);
            });
        }
    }
}