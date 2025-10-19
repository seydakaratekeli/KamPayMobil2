using KamPay.ViewModels;

namespace KamPay.Views;

public partial class ProductListPage : ContentPage
{
    private bool _isInitialized = false;

    public ProductListPage(ProductListViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ProductListViewModel vm)
        {
            // Bu kontrol, sayfa ilk olu�turuldu�unda metodun iki kez
            // (hem constructor hem de OnAppearing taraf�ndan) �a�r�lmas�n� �nler.
            // Sayfaya geri d�n�ld���nde ise dinleyiciyi yeniden ba�lat�r.
            if (_isInitialized)
            {
                vm.InitializeViewModel();
            }
            _isInitialized = true;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    // Varsa bu metodu silebilir veya tutabilirsiniz, art�k gerekli de�il.
    // private async void OnBackClicked(object sender, EventArgs e)
    // {
    //     await Shell.Current.GoToAsync("..");
    // }
}