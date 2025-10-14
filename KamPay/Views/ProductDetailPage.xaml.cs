using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.Services;
using KamPay.ViewModels;

namespace KamPay.Views;

public partial class ProductDetailPage : ContentPage
{
    private readonly IServiceProvider _serviceProvider;

    public ProductDetailPage(ProductDetailViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _serviceProvider = serviceProvider; // DI Container'ý alýyoruz

        // Mesajý dinlemeye baþla
        WeakReferenceMessenger.Default.Register<ShowTradeOfferPopupMessage>(this, async (r, m) =>
        {
            // Gerekli servisleri DI container'dan al
            var tradeViewModel = new TradeOfferViewModel(
                _serviceProvider.GetRequiredService<IProductService>(),
                _serviceProvider.GetRequiredService<ITransactionService>(),
                _serviceProvider.GetRequiredService<IAuthenticationService>())
            {
                ProductId = m.TargetProduct.ProductId
            };

            var tradePopup = new TradeOfferView(tradeViewModel);

            // Pop-up'ý göster
            await this.ShowPopupAsync(tradePopup);
        });
    }

    // Sayfadan ayrýlýrken mesaj dinleyicisini kapat
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        WeakReferenceMessenger.Default.Unregister<ShowTradeOfferPopupMessage>(this);
    }
}