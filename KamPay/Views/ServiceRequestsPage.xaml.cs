using KamPay.Models;
using KamPay.ViewModels;

namespace KamPay.Views;

public partial class ServiceRequestsPage : ContentPage
{
    public ServiceRequestsPage(ServiceRequestsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ServiceRequestsViewModel vm && vm.LoadMyRequestsCommand.CanExecute(null))
        {
            vm.LoadMyRequestsCommand.Execute(null);
        }
    }

    // "Kabul Et" butonuna t�kland���nda �al���r.
    private void AcceptButton_Clicked(object sender, System.EventArgs e)
    {
        if (sender is Button button && button.BindingContext is ServiceRequest request)
        {
            if (this.BindingContext is ServiceRequestsViewModel vm)
            {
                // ViewModel'deki komutu 'true' parametresiyle (kabul) �a��r.
                vm.RespondToRequestCommand.Execute(new Tuple<string, bool>(request.RequestId, true));
            }
        }
    }

    // "Reddet" butonuna t�kland���nda �al���r.
    private void DeclineButton_Clicked(object sender, System.EventArgs e)
    {
        if (sender is Button button && button.BindingContext is ServiceRequest request)
        {
            if (this.BindingContext is ServiceRequestsViewModel vm)
            {
                // ViewModel'deki komutu 'false' parametresiyle (reddet) �a��r.
                vm.RespondToRequestCommand.Execute(new Tuple<string, bool>(request.RequestId, false));
            }
        }
    }
}