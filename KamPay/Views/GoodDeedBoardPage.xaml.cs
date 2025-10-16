using KamPay.ViewModels;

namespace KamPay.Views;

public partial class GoodDeedBoardPage : ContentPage
{
    public GoodDeedBoardPage(GoodDeedBoardViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}