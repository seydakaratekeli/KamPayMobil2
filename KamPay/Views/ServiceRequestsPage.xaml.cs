using KamPay.ViewModels;

namespace KamPay.Views
{
    public partial class ServiceRequestsPage : ContentPage
    {
        private readonly ServiceRequestsViewModel _viewModel;

        public ServiceRequestsPage(ServiceRequestsViewModel vm)
        {
            InitializeComponent();
            _viewModel = vm;
            BindingContext = _viewModel;
        }

        // ✅ Picker event handler - güvenli null check
        private void OnPaymentMethodSelected(object sender, EventArgs e)
        {
            if (sender is Picker picker &&
                picker.SelectedItem is ServiceRequestsViewModel.PaymentOption option)
            {
                // 🔥 Direkt _viewModel kullan, BindingContext'e güvenme
                if (_viewModel != null)
                {
                    _viewModel.SelectedPaymentMethod = option.Method;
                    System.Diagnostics.Debug.WriteLine($"💳 Ödeme yöntemi seçildi: {option.DisplayName}");
                }
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            System.Diagnostics.Debug.WriteLine("✅ ServiceRequestsPage: Aktif");
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // 🔥 Burada Dispose ETME!
            System.Diagnostics.Debug.WriteLine("⏸️ ServiceRequestsPage: Arka plana alındı");
        }

        // 🔥 Sayfa bellekten kaldırılınca otomatik çağrılır
        ~ServiceRequestsPage()
        {
            _viewModel?.Dispose();
            System.Diagnostics.Debug.WriteLine("🗑️ ServiceRequestsPage: Dispose edildi");
        }
    }
}