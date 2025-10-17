using KamPay.ViewModels;

namespace KamPay.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    
    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Sayfa her g�r�nd���nde ViewModel'deki metodu �a��rarak
        // email ve �ifre kutular�n� temizle.
        if (BindingContext is LoginViewModel vm)
        {
            vm.ClearCredentials();
        }
    }
}