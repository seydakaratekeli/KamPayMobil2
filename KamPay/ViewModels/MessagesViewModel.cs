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
        private bool _isInitialized = false;

        [ObservableProperty]
        private bool isLoading = true;

        [ObservableProperty]
        private int unreadCount;

        [ObservableProperty]
        private string emptyMessage = "Henüz mesajınız yok";

        public ObservableCollection<Conversation> Conversations { get; } = new();

        public MessagesViewModel(IMessagingService messagingService, IAuthenticationService authService)
        {
            _messagingService = messagingService;
            _authService = authService;
        }

        // 🔥 Sayfa göründüğünde otomatik çağrılacak
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            IsLoading = true;
            try
            {
                // 🔴 BURADA EKSİKTİ: _currentUser set edilmiyordu!
                _currentUser = await _authService.GetCurrentUserAsync();

                if (_currentUser == null)
                {
                    EmptyMessage = "Mesajları görmek için giriş yapmalısınız.";
                    IsLoading = false;
                    return;
                }

                await StartListeningForConversationsAsync();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Konuşmalar yüklenirken hata oluştu: {ex.Message}");
                EmptyMessage = "Konuşmalar yüklenemedi.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task StartListeningForConversationsAsync()
        {
            if (_currentUser == null)
            {
                Console.WriteLine("⚠️ _currentUser null, listener başlatılamadı!");
                return;
            }

            Conversations.Clear();

            // 🔥 Real-time Firebase listener
            _conversationsSubscription = _firebaseClient
                .Child(Constants.ConversationsCollection)
                .AsObservable<Conversation>()
                .Where(e => e.Object != null &&
                           e.Object.IsActive &&
                           (e.Object.User1Id == _currentUser.UserId || e.Object.User2Id == _currentUser.UserId))
                .Subscribe(e =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
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
                                    // Var olanı güncelle
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

                            // Listeyi LastMessageTime'a göre sırala
                            var sortedList = Conversations.OrderByDescending(c => c.LastMessageTime).ToList();
                            Conversations.Clear();
                            foreach (var item in sortedList)
                            {
                                Conversations.Add(item);
                            }

                            // Toplam okunmamış sayısını yeniden hesapla
                            UnreadCount = Conversations.Sum(c => c.UnreadCount);
                            WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(UnreadCount > 0));

                            EmptyMessage = Conversations.Any() ? string.Empty : "Henüz mesajınız yok.";
                            IsLoading = false;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Conversation update hatası: {ex.Message}");
                        }
                    });
                },
                error =>
                {
                    Console.WriteLine($"❌ Firebase listener hatası: {error.Message}");
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        EmptyMessage = "Konuşmalar yüklenirken hata oluştu.";
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
                "Bu konuşmayı silmek istediğinize emin misiniz?",
                "Evet",
                "Hayır"
            );

            if (!confirm) return;

            try
            {
                var currentUser = await _authService.GetCurrentUserAsync();
                var result = await _messagingService.DeleteConversationAsync(conversation.ConversationId, currentUser.UserId);

                if (result.Success)
                {
                    // Real-time listener otomatik güncelleyecek, manuel silmeye gerek yok
                    // Conversations.Remove(conversation); 
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
        }

        [RelayCommand]
        async Task GoToChatAsync(Conversation conversation)
        {
            if (conversation == null) return;
            await Shell.Current.GoToAsync($"{nameof(ChatPage)}?conversationId={conversation.ConversationId}");
        }

        public void Dispose()
        {
            _conversationsSubscription?.Dispose();
        }
    }
}