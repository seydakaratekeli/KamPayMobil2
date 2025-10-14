using KamPay.ViewModels;
using CommunityToolkit.Mvvm.Messaging; // EKLENDÝ

namespace KamPay.Views;

public partial class MessagesPage : ContentPage
{
    public MessagesPage(MessagesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // ===== YENÝ METOT EKLENDÝ =====
    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Bu sayfa göründüðünde, okunmamýþ mesaj rozetini gizlemesi için sinyal gönder.
        WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(false));
    }
    // =============================
}