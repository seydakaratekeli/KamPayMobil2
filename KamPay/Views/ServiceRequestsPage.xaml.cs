using KamPay.ViewModels;

namespace KamPay.Views;

public partial class ServiceRequestsPage : ContentPage
{
    public ServiceRequestsPage(ServiceRequestsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // G�rd���n�z gibi, OnAppearing, AcceptButton_Clicked ve
    // DeclineButton_Clicked metotlar�n�n HEPS�N� S�LD�K.
    // ��nk� art�k bu i�leri do�rudan XAML hallediyor.
}