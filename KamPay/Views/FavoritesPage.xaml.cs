
using KamPay.ViewModels;

namespace KamPay.Views;

public partial class FavoritesPage : ContentPage
{
    public FavoritesPage(FavoritesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // YENÝ: OnAppearing metodu eklendi
    protected override void OnAppearing()
    {
        base.OnAppearing();
        // ViewModel "partial" olduðu için "vm.InitializeCommand" artýk bulunacak ve hata vermeyecektir.
        if (BindingContext is FavoritesViewModel vm && vm.InitializeCommand.CanExecute(null))
        {
            vm.InitializeCommand.Execute(null);
        }
    }

    // Mevcut OnDisappearing metodu AYNEN KALIYOR
    protected override void OnDisappearing()
    {
        base.OnAppearing(); // DÝKKAT: Burada bir kopyala-yapýþtýr hatasý olmuþ olabilir, OnDisappearing metodu base.OnDisappearing() çaðýrmalýdýr.
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}