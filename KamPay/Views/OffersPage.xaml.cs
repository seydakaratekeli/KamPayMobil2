using KamPay.ViewModels;

namespace KamPay.Views;

public partial class OffersPage : ContentPage
{
    public OffersPage(OffersViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}