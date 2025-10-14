using KamPay.ViewModels;
using Microsoft.Maui.Controls;

namespace KamPay.Views
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage(LoginViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
        }
    }
}
