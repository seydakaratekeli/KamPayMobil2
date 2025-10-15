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
}
