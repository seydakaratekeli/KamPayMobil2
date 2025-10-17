using KamPay.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using System; // IDisposable için eklendi

namespace KamPay.Views;

public partial class MessagesPage : ContentPage
{
    public MessagesPage(MessagesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Bu sayfa göründüðünde, okunmamýþ mesaj rozetini gizlemesi için sinyal gönder.
        WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(false));
    }

    // --- YENÝ EKLENEN KISIM ---
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}