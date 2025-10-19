using KamPay.ViewModels;

namespace KamPay.Views;

public partial class AddProductPage : ContentPage
{
    public AddProductPage(AddProductViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is AddProductViewModel vm && vm.LoadCategoriesCommand.CanExecute(null))
        {
            vm.LoadCategoriesCommand.Execute(null);
        }
    }
}
