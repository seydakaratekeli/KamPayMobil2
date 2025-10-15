using CommunityToolkit.Mvvm.Messaging; 
using KamPay.ViewModels;
using KamPay.Models;

namespace KamPay.Views;

public class ScrollToChatMessage
{
    public Message Message { get; }
    public ScrollToChatMessage(Message message) => Message = message;
}

public partial class ChatPage : ContentPage
{
   

    public ChatPage(ChatViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        WeakReferenceMessenger.Default.Register<ScrollToChatMessage>(this, (r, m) =>
        {
            ScrollToLastItem(m.Message);
        });

        // Sayfa ilk açýldýðýnda da en alta kaydýr (örneðin son mesajý)
        await Task.Delay(200);
        if (BindingContext is ChatViewModel vm && vm.Messages.Any())
        {
            var last = vm.Messages.Last();
            ScrollToLastItem(last);
        }
    }

    // Sayfa kaybolduðunda mesaj dinleyicisini durdur
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Bellek sýzýntýsý olmamasý için kaydý temizle
        WeakReferenceMessenger.Default.Unregister<ScrollToChatMessage>(this);
    }

    private async void ScrollToLastItem(Message message)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (MessagesCollectionView != null && message != null)
                {
                    // Küçük bir gecikme ekle (UI render için zaman tanýr)
                    await Task.Delay(100);
                    MessagesCollectionView.ScrollTo(message, position: ScrollToPosition.End, animate: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scroll hatasý: {ex.Message}");
            }
        });
    }

}