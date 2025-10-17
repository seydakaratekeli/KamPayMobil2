using KamPay.ViewModels;
using System; // IDisposable için eklendi

namespace KamPay.Views;

public partial class OffersPage : ContentPage
{
    public OffersPage(OffersViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
    // KamPay/Views/OffersPage.xaml.cs
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}