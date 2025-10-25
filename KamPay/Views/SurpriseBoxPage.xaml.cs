using KamPay.ViewModels;

namespace KamPay.Views;

public partial class SurpriseBoxPage : ContentPage
{
    private readonly SurpriseBoxViewModel _viewModel;

    public SurpriseBoxPage(SurpriseBoxViewModel vm)
    {
        InitializeComponent();
        _viewModel = vm;
        BindingContext = _viewModel;

        _viewModel.RedemptionCompleted += OnRedemptionCompleted;
    }

    // 🔥 Sayfa her göründüğünde puanları yenile
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshAsync();
    }

    private async void OnRedemptionCompleted(object sender, bool success)
    {
        if (success)
        {
            // Animasyon
            await BoxImage.RotateTo(-15, 100);
            await BoxImage.RotateTo(15, 100);
            await BoxImage.RotateTo(-10, 100);
            await BoxImage.RotateTo(10, 100);
            await BoxImage.RotateTo(0, 100);

            // Sonuçları göster
            ResultFrame.IsVisible = true;
            await ResultFrame.FadeTo(1, 400);
        }
    }

    private async void CloseResult_Clicked(object sender, EventArgs e)
    {
        await ResultFrame.FadeTo(0, 400);
        ResultFrame.IsVisible = false;

        // 🔥 YENİ: Kazanılan ürünün detay sayfasına git
        if (_viewModel.RedemptionResult != null)
        {
            await Shell.Current.GoToAsync($"ProductDetailPage?productId={_viewModel.RedemptionResult.ProductId}");
        }

        _viewModel.ResetCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.RedemptionCompleted -= OnRedemptionCompleted;
    }
}