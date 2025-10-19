using KamPay.ViewModels;

namespace KamPay.Views;

public partial class ServiceRequestsPage : ContentPage
{
    public ServiceRequestsPage(ServiceRequestsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // Gördüðünüz gibi, OnAppearing, AcceptButton_Clicked ve
    // DeclineButton_Clicked metotlarýnýn HEPSÝNÝ SÝLDÝK.
    // Çünkü artýk bu iþleri doðrudan XAML hallediyor.
}