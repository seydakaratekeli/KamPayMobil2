
using KamPay.ViewModels;

namespace KamPay.Views;

public partial class FavoritesPage : ContentPage
{
    public FavoritesPage(FavoritesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // YEN�: OnAppearing metodu eklendi
    protected override void OnAppearing()
    {
        base.OnAppearing();
        // ViewModel "partial" oldu�u i�in "vm.InitializeCommand" art�k bulunacak ve hata vermeyecektir.
        if (BindingContext is FavoritesViewModel vm && vm.InitializeCommand.CanExecute(null))
        {
            vm.InitializeCommand.Execute(null);
        }
    }

    // Mevcut OnDisappearing metodu AYNEN KALIYOR
    protected override void OnDisappearing()
    {
        base.OnAppearing(); // D�KKAT: Burada bir kopyala-yap��t�r hatas� olmu� olabilir, OnDisappearing metodu base.OnDisappearing() �a��rmal�d�r.
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}