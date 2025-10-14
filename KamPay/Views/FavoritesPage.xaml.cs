using KamPay.ViewModels;
using Microsoft.Maui.Controls;

namespace KamPay.Views;

public partial class FavoritesPage : ContentPage
{
    public FavoritesPage(FavoritesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }


}

