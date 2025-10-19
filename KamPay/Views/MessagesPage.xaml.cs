// KamPay/Views/MessagesPage.xaml.cs

using KamPay.ViewModels;

namespace KamPay.Views;

public partial class MessagesPage : ContentPage
{
    public MessagesPage(MessagesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // YENÝ: OnAppearing metodu eklendi
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MessagesViewModel vm && vm.InitializeCommand.CanExecute(null))
        {
            vm.InitializeCommand.Execute(null);
        }
    }

    // Mevcut OnDisappearing metodu AYNEN KALIYOR
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}