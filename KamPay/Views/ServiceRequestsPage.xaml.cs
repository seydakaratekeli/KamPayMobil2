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

    // G�rd���n�z gibi, OnAppearing, AcceptButton_Clicked ve
    // DeclineButton_Clicked metotlar�n�n HEPS�N� S�LD�K.
    // ��nk� art�k bu i�leri do�rudan XAML hallediyor.
}