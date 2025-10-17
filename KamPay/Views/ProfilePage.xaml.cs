using KamPay.ViewModels;
using Microsoft.Maui.Controls;

namespace KamPay.Views;

public partial class ProfilePage : ContentPage
{
    public ProfilePage(ProfileViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

   
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ProfileViewModel vm)
        {
            // Komutu �a��rarak profil verilerini yeniden y�kle
            if (vm.LoadProfileCommand.CanExecute(null))
            {
                vm.LoadProfileCommand.Execute(null);
            }
        }
    }
}