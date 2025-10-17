using KamPay.ViewModels;

namespace KamPay.Views;

public partial class SurpriseBoxPage : ContentPage
{
    public SurpriseBoxPage(SurpriseBoxViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        // ViewModel'de i�lem tamamland���nda animasyonu tetikle
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

            // Sonu�lar� g�ster
            ResultFrame.IsVisible = true;
            await ResultFrame.FadeTo(1, 400);
        }
    }

    private async void CloseResult_Clicked(object sender, EventArgs e)
    {
        await ResultFrame.FadeTo(0, 400);
        ResultFrame.IsVisible = false;

        // ViewModel'i s�f�rla
        if (BindingContext is SurpriseBoxViewModel vm)
        {
            vm.ResetCommand.Execute(null);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Event aboneli�ini kald�rarak bellek s�z�nt�s�n� �nle
        if (BindingContext is SurpriseBoxViewModel vm)
        {
            vm.RedemptionCompleted -= OnRedemptionCompleted;
        }
    }
}