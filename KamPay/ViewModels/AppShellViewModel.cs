// KamPay/ViewModels/AppShellViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.Services;
using KamPay.Helpers;
using Firebase.Database;
using Firebase.Database.Query;
using System.Reactive.Linq;
using KamPay.Models;

namespace KamPay.ViewModels
{
    public partial class AppShellViewModel : ObservableObject, IDisposable
    {
        [ObservableProperty]
        private bool hasUnreadNotifications;

        [ObservableProperty]
        private bool hasUnreadMessages;

        private readonly IAuthenticationService _authService;
        private readonly IMessagingService _messagingService;
        private IDisposable? _messageSubscription;

        // FirebaseClient'� her seferinde yeniden olu�turmak yerine bir kere olu�turup kullanmak daha verimlidir.
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);


        public AppShellViewModel(IAuthenticationService authService, IMessagingService messagingService)
        {
            _authService = authService;
            _messagingService = messagingService;

            // Genel bildirimleri dinle
            WeakReferenceMessenger.Default.Register<UnreadGeneralNotificationStatusMessage>(this, (r, m) =>
            {
                HasUnreadNotifications = m.Value;
            });

            // Mesaj bildirimlerini dinle (Bu mesaj �u anki kodda kullan�lm�yor, ancak gelecekte kullan�labilir)
            WeakReferenceMessenger.Default.Register<UnreadMessageStatusMessage>(this, (r, m) =>
            {
                HasUnreadMessages = m.Value;
            });

            // G�NCELLEND�: Kullan�c� giri� / ��k�� yapt���nda asenkron olarak tepki ver
            WeakReferenceMessenger.Default.Register<UserSessionChangedMessage>(this, async (r, m) =>
            {
                if (m.Value) // Giri� yap�ld�
                {
                    // Art�k metodu g�venle 'await' edebiliriz
                    await StartListeningForMessagesAsync();
                }
                else // ��k�� yap�ld�
                {
                    StopListeningForMessages();
                    HasUnreadMessages = false;
                }
            });
        }

        // G�NCELLEND�: Metodun imzas� async Task olarak de�i�tirildi
        private async Task StartListeningForMessagesAsync()
        {
            StopListeningForMessages(); // �nceki dinleyiciyi durdur

            var currentUser = await _authService.GetCurrentUserAsync();
            if (currentUser == null) return;

            // Uygulama a��ld���nda ilk kontrol� yap
            var initialCheckResult = await _messagingService.GetTotalUnreadMessageCountAsync(currentUser.UserId);
            if (initialCheckResult.Success)
            {
                HasUnreadMessages = initialCheckResult.Data > 0;
            }

            // Ger�ek zamanl� dinleyiciyi ba�lat
            _messageSubscription = _firebaseClient
                .Child(Constants.ConversationsCollection)
                .AsObservable<Conversation>()
                .Where(e => e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate &&
                             e.Object != null &&
                             (e.Object.User1Id == currentUser.UserId || e.Object.User2Id == currentUser.UserId))
                .Subscribe(async entry =>
                {
                    // Kullan�c�ya ait bir konu�ma g�ncellendi�inde, toplam okunmam�� say�s�n� yeniden kontrol et
                    var result = await _messagingService.GetTotalUnreadMessageCountAsync(currentUser.UserId);
                    if (result.Success)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            HasUnreadMessages = result.Data > 0;
                        });
                    }
                });
        }

        private void StopListeningForMessages()
        {
            _messageSubscription?.Dispose();
            _messageSubscription = null;
        }

        public void Dispose()
        {
            // Bellekte kalan abonelikleri temizle
            StopListeningForMessages();
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
    }

    // --- Mesaj S�n�flar� ---
    // Bu s�n�flar�n ayr� bir dosyada olmas� daha temiz bir yap� sa�lar,
    // ancak �imdilik burada kalabilirler.

    public class UnreadGeneralNotificationStatusMessage : CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<bool>
    {
        public UnreadGeneralNotificationStatusMessage(bool value) : base(value) { }
    }

    public class UnreadMessageStatusMessage : CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<bool>
    {
        public UnreadMessageStatusMessage(bool value) : base(value) { }
    }

    public class UserSessionChangedMessage : CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<bool>
    {
        public UserSessionChangedMessage(bool isLoggedIn) : base(isLoggedIn) { }
    }
}