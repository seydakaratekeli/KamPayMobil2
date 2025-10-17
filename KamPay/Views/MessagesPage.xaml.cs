using KamPay.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using System; // IDisposable i�in eklendi

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
        // Bu sayfa g�r�nd���nde, okunmam�� mesaj rozetini gizlemesi i�in sinyal g�nder.
        WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(false));
    }

    // --- YEN� EKLENEN KISIM ---
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}