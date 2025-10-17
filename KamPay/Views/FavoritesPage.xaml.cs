using KamPay.ViewModels;
using Microsoft.Maui.Controls;
using System; // IDisposable için eklendi

namespace KamPay.Views;

public partial class FavoritesPage : ContentPage
{
    public FavoritesPage(FavoritesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // KamPay/Views/FavoritesPage.xaml.cs
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

