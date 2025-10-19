// KamPay/Views/GoodDeedBoardPage.xaml.cs
using KamPay.ViewModels;

namespace KamPay.Views;

public partial class GoodDeedBoardPage : ContentPage
{
    public GoodDeedBoardPage(GoodDeedBoardViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Sayfa her g�r�nd���nde, ViewModel'deki dinleyiciyi ba�lat.
        if (BindingContext is GoodDeedBoardViewModel vm)
        {
            vm.StartListeningForPosts();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Sayfa her gizlendi�inde, kaynaklar� bo�a harcamamak i�in dinleyiciyi durdur.
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose(); // Bu, ViewModel'deki StopListening'i �a��r�r
        }
    }
}