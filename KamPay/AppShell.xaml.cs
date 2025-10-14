using KamPay.Views;
using KamPay.ViewModels; // Yeni eklediğimiz ViewModel için

namespace KamPay
{
    public partial class AppShell : Shell
    {
        public AppShell(AppShellViewModel vm) // Constructor'a ViewModel'i enjekte et
        {
            InitializeComponent();
            BindingContext = vm; // Shell'in BindingContext'ini ayarla

            // Rota Kayıtları: Sekmelerde olmayan ama gidilecek sayfalar
            Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));
            Routing.RegisterRoute(nameof(AddProductPage), typeof(AddProductPage));
            Routing.RegisterRoute(nameof(EditProductPage), typeof(EditProductPage));
            Routing.RegisterRoute(nameof(ProductDetailPage), typeof(ProductDetailPage));
            Routing.RegisterRoute(nameof(ChatPage), typeof(ChatPage));
            Routing.RegisterRoute(nameof(NotificationsPage), typeof(NotificationsPage));
            // Mevcut Routing.RegisterRoute'ların altına ekle
            Routing.RegisterRoute(nameof(OffersPage), typeof(OffersPage));
            Routing.RegisterRoute(nameof(TradeOfferView), typeof(TradeOfferView));
        }
    }
}