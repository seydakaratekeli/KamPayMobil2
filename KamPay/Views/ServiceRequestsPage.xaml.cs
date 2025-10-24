using KamPay.ViewModels;

namespace KamPay.Views;

public partial class ServiceRequestsPage : ContentPage
{
    public ServiceRequestsPage(ServiceRequestsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void OnPaymentMethodSelected(object sender, EventArgs e)
    {
        var picker = sender as Picker;
        if (picker?.SelectedItem is ServiceRequestsViewModel.PaymentOption option)
        {
            var vm = BindingContext as ServiceRequestsViewModel;
            if (vm != null)
                vm.SelectedPaymentMethod = option.Method;
        }
    }

    // Gördüðünüz gibi, OnAppearing, AcceptButton_Clicked ve
    // DeclineButton_Clicked metotlarýnýn HEPSÝNÝ SÝLDÝK.
    // Çünkü artýk bu iþleri doðrudan XAML hallediyor.
}