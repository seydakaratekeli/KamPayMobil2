using KamPay.ViewModels;

namespace KamPay.Views;

public partial class ProductListPage : ContentPage
{
    public ProductListPage(ProductListViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
    // KamPay/Views/ProductListPage.xaml.cs
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}