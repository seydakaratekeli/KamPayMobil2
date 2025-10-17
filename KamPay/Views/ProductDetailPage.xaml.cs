using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.Services;
using KamPay.ViewModels;
using System; // Bu using ifadesini ekleyin

namespace KamPay.Views;

public partial class ProductDetailPage : ContentPage
{
    private readonly IServiceProvider _serviceProvider;

    public ProductDetailPage(ProductDetailViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _serviceProvider = serviceProvider; // DI Container'� al�yoruz

        WeakReferenceMessenger.Default.Register<ShowTradeOfferPopupMessage>(this, async (r, m) =>
        {
            // --- D�ZELT�LM�� KISIM ---
            // Art�k 'new' anahtar kelimesiyle nesne olu�turmuyoruz.
            // Bunun yerine, DI container'dan bir 'TradeOfferView' �rne�i talep ediyoruz.
            var tradePopup = _serviceProvider.GetRequiredService<TradeOfferView>();

            // Popup'�n ViewModel'ine eri�ip, takas yap�lacak �r�n�n ID'sini at�yoruz.
            if (tradePopup.BindingContext is TradeOfferViewModel vm)
            {
                vm.ProductId = m.TargetProduct.ProductId;
            }

            // Popup'� kullan�c�ya g�steriyoruz.
            await this.ShowPopupAsync(tradePopup);
        });
    }

    // Sayfadan ayr�l�rken (�rne�in geri tu�una bas�ld���nda) mesaj dinleyicisini kapat�yoruz.
    // Bu, bellek s�z�nt�lar�n� (memory leaks) �nlemek i�in kritik bir ad�md�r.
    // protected override void OnDisappearing()
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        WeakReferenceMessenger.Default.Unregister<ShowTradeOfferPopupMessage>(this);
    }
}