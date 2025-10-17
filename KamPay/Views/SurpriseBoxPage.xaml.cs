using KamPay.ViewModels;

namespace KamPay.Views;

public partial class SurpriseBoxPage : ContentPage
{
    public SurpriseBoxPage(SurpriseBoxViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        // ViewModel'de iþlem tamamlandýðýnda animasyonu tetikle
        vm.RedemptionCompleted += OnRedemptionCompleted;
    }

    private async void OnRedemptionCompleted(object sender, bool success)
    {
        if (success)
        {
            // Basit bir sallanma animasyonu
            await BoxImage.RotateTo(-15, 100);
            await BoxImage.RotateTo(15, 100);
            await BoxImage.RotateTo(-10, 100);
            await BoxImage.RotateTo(10, 100);
            await BoxImage.RotateTo(0, 100);

            // Sonuçlarý göster
            ResultFrame.IsVisible = true;
            await ResultFrame.FadeTo(1, 400);
        }
    }

    private async void CloseResult_Clicked(object sender, EventArgs e)
    {
        await ResultFrame.FadeTo(0, 400);
        ResultFrame.IsVisible = false;

        // ViewModel'i sýfýrla
        if (BindingContext is SurpriseBoxViewModel vm)
        {
            vm.ResetCommand.Execute(null);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Event aboneliðini kaldýrarak bellek sýzýntýsýný önle
        if (BindingContext is SurpriseBoxViewModel vm)
        {
            vm.RedemptionCompleted -= OnRedemptionCompleted;
        }
    }
}