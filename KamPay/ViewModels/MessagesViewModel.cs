using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.Models;
using KamPay.Services;
using KamPay.Views;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using System.Reactive.Linq;

namespace KamPay.ViewModels
{
    public partial class MessagesViewModel : ObservableObject, IDisposable
    {
        private readonly IMessagingService _messagingService;
        private readonly IAuthenticationService _authService;
        private IDisposable _conversationsSubscription;
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);
        private User _currentUser;

        [ObservableProperty]
        private bool isLoading = true;


        [ObservableProperty]
        private int unreadCount;

        [ObservableProperty]
        private string emptyMessage = "Henüz mesajýnýz yok";

        public ObservableCollection<Conversation> Conversations { get; } = new();

        public MessagesViewModel(IMessagingService messagingService, IAuthenticationService authService)
        {
            _messagingService = messagingService;
            _authService = authService;

            StartListeningForConversations();
        }

        private async void StartListeningForConversations()
        {
            IsLoading = true;
            _currentUser = await _authService.GetCurrentUserAsync();
            if (_currentUser == null)
            {
                IsLoading = false;
                EmptyMessage = "Sohbetleri görmek için giriþ yapmalýsýnýz.";
                return;
            }

            Conversations.Clear();

            _conversationsSubscription = _firebaseClient
                .Child(Constants.ConversationsCollection)
                .OrderBy("LastMessageTime")
                .AsObservable<Conversation>()
                .Where(e => e.Object != null && e.Object.IsActive && (e.Object.User1Id == _currentUser.UserId || e.Object.User2Id == _currentUser.UserId))
                .Subscribe(e =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var conversation = e.Object;
                        conversation.ConversationId = e.Key;
                        conversation.OtherUserName = conversation.GetOtherUserName(_currentUser.UserId);
                        conversation.OtherUserPhotoUrl = conversation.GetOtherUserPhotoUrl(_currentUser.UserId);
                        conversation.UnreadCount = conversation.GetUnreadCount(_currentUser.UserId);

                        var existingConvo = Conversations.FirstOrDefault(c => c.ConversationId == conversation.ConversationId);

                        if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate)
                        {
                            if (existingConvo != null)
                            {
                                // Var olaný güncelle
                                var index = Conversations.IndexOf(existingConvo);
                                Conversations[index] = conversation;
                            }
                            else
                            {
                                // Yeni sohbeti en üste ekle
                                Conversations.Insert(0, conversation);
                            }
                        }
                        else if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.Delete)
                        {
                            if (existingConvo != null)
                            {
                                Conversations.Remove(existingConvo);
                            }
                        }

                        // Toplam okunmamýþ sayýsýný yeniden hesapla
                        UnreadCount = Conversations.Sum(c => c.UnreadCount);
                        WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(UnreadCount > 0));

                        EmptyMessage = Conversations.Any() ? string.Empty : "Henüz mesajýnýz yok.";
                        IsLoading = false;
                    });
                });
        }

        [RelayCommand]
        private async Task ConversationTappedAsync(Conversation conversation)
        {
            if (conversation == null) return;
            await Shell.Current.GoToAsync($"{nameof(ChatPage)}?conversationId={conversation.ConversationId}");
        }

        [RelayCommand]
        private async Task DeleteConversationAsync(Conversation conversation)
        {
            if (conversation == null) return;

            var confirm = await Application.Current.MainPage.DisplayAlert(
                "Onay",
                "Bu konuþmayý silmek istediðinize emin misiniz?",
                "Evet",
                "Hayýr"
            );

            if (!confirm) return;

            try
            {
                var currentUser = await _authService.GetCurrentUserAsync();
                var result = await _messagingService.DeleteConversationAsync(conversation.ConversationId, currentUser.UserId);

                if (result.Success)
                {
                    Conversations.Remove(conversation);
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
        }

        public void Dispose()
        {
            _conversationsSubscription?.Dispose();
        }
    }

    }