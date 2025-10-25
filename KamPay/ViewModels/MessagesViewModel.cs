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

        // 🔥 CACHE: Conversation ID tracker (중복 방지)
        private readonly HashSet<string> _conversationIds = new();

        [ObservableProperty]
        private bool isLoading = true;

        [ObservableProperty]
        private bool isRefreshing = false;

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

        // 🔥 İlk yükleme sadece bir kez
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            IsLoading = true;
            try
            {
                _currentUser = await _authService.GetCurrentUserAsync();

                if (_currentUser == null)
                {
                    EmptyMessage = "Mesajları görmek için giriş yapmalısınız.";
                    IsLoading = false;
                    return;
                }

                StartListeningForConversations();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ InitializeAsync hatası: {ex.Message}");
                EmptyMessage = "Konuşmalar yüklenemedi.";
                IsLoading = false;
            }
        }

        // 🔥 OPTİMİZE: Batch processing + HashSet中복 체크
        private void StartListeningForConversations()
        {
            if (_currentUser == null)
            {
                Console.WriteLine("⚠️ _currentUser null, listener başlatılamadı!");
                return;
            }

            Console.WriteLine("🔥 Conversations listener başlatılıyor...");

            _conversationsSubscription = _firebaseClient
                .Child(Constants.ConversationsCollection)
                .AsObservable<Conversation>()
                .Where(e => e.Object != null &&
                           e.Object.IsActive &&
                           (e.Object.User1Id == _currentUser.UserId || e.Object.User2Id == _currentUser.UserId))
                .Buffer(TimeSpan.FromMilliseconds(250)) // 🔥 250ms batch (daha kısa, mesajlar critical)
                .Where(batch => batch.Any())
                .Subscribe(
                    events =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                ProcessConversationBatch(events);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Conversation batch hatası: {ex.Message}");
                            }
                            finally
                            {
                                IsLoading = false;
                                IsRefreshing = false;
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
                            IsRefreshing = false;
                        });
                    });
        }

        // 🔥 OPTİMİZE: Duplicate check + Smart update
        private void ProcessConversationBatch(IList<Firebase.Database.Streaming.FirebaseEvent<Conversation>> events)
        {
            bool hasChanges = false;

            foreach (var e in events)
            {
                var conversation = e.Object;
                conversation.ConversationId = e.Key;
                conversation.OtherUserName = conversation.GetOtherUserName(_currentUser.UserId);
                conversation.OtherUserPhotoUrl = conversation.GetOtherUserPhotoUrl(_currentUser.UserId);
                conversation.UnreadCount = conversation.GetUnreadCount(_currentUser.UserId);

                var existingConvo = Conversations.FirstOrDefault(c => c.ConversationId == conversation.ConversationId);

                switch (e.EventType)
                {
                    case Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate:
                        if (existingConvo != null)
                        {
                            // 🔥 Güncelleme - pozisyonu koru (sorting sonra yapılacak)
                            var index = Conversations.IndexOf(existingConvo);
                            Conversations[index] = conversation;
                        }
                        else
                        {
                            // 🔥 Yeni ekleme - duplicate check
                            if (!_conversationIds.Contains(conversation.ConversationId))
                            {
                                Conversations.Add(conversation);
                                _conversationIds.Add(conversation.ConversationId);
                            }
                        }
                        hasChanges = true;
                        break;

                    case Firebase.Database.Streaming.FirebaseEventType.Delete:
                        if (existingConvo != null)
                        {
                            Conversations.Remove(existingConvo);
                            _conversationIds.Remove(conversation.ConversationId);
                            hasChanges = true;
                        }
                        break;
                }
            }

            // 🔥 Sadece değişiklik varsa sırala + unread güncelle
            if (hasChanges)
            {
                SortConversationsInPlace();
                UpdateUnreadCount();
                EmptyMessage = Conversations.Any() ? string.Empty : "Henüz mesajınız yok.";
            }
        }

        // 🔥 OPTİMİZE: In-place sorting (Clear() YOK)
        private void SortConversationsInPlace()
        {
            var sorted = Conversations.OrderByDescending(c => c.LastMessageTime).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var currentIndex = Conversations.IndexOf(sorted[i]);
                if (currentIndex != i && currentIndex >= 0)
                {
                    Conversations.Move(currentIndex, i);
                }
            }
        }

        // 🔥 Unread count güncelle
        private void UpdateUnreadCount()
        {
            UnreadCount = Conversations.Sum(c => c.UnreadCount);
            WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(UnreadCount > 0));
        }

        // 🔥 Pull-to-Refresh Command
        [RelayCommand]
        private async Task RefreshConversationsAsync()
        {
            if (IsRefreshing) return;

            try
            {
                IsRefreshing = true;

                // Tüm konuşmaları tekrar çek
                var result = await _messagingService.GetUserConversationsAsync(_currentUser.UserId);

                if (result.Success && result.Data != null)
                {
                    // 🔥 Smart update
                    UpdateConversationsFromRefresh(result.Data);

                    UpdateUnreadCount();
                    EmptyMessage = Conversations.Any() ? string.Empty : "Henüz mesajınız yok.";
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata",
                        result.Message ?? "Konuşmalar yüklenemedi", "Tamam");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Refresh hatası: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert("Hata",
                    "Konuşmalar yenilenirken bir hata oluştu.", "Tamam");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        // 🔥 YENİ: Refresh'ten gelen data ile smart update
        private void UpdateConversationsFromRefresh(List<Conversation> freshData)
        {
            // 1️⃣ Silinmesi gerekenler
            for (int i = Conversations.Count - 1; i >= 0; i--)
            {
                if (!freshData.Any(c => c.ConversationId == Conversations[i].ConversationId))
                {
                    _conversationIds.Remove(Conversations[i].ConversationId);
                    Conversations.RemoveAt(i);
                }
            }

            // 2️⃣ Güncellenecek veya eklenecekler
            foreach (var freshConvo in freshData)
            {
                freshConvo.OtherUserName = freshConvo.GetOtherUserName(_currentUser.UserId);
                freshConvo.OtherUserPhotoUrl = freshConvo.GetOtherUserPhotoUrl(_currentUser.UserId);
                freshConvo.UnreadCount = freshConvo.GetUnreadCount(_currentUser.UserId);

                var existingIndex = -1;
                for (int i = 0; i < Conversations.Count; i++)
                {
                    if (Conversations[i].ConversationId == freshConvo.ConversationId)
                    {
                        existingIndex = i;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    // Güncelleme
                    Conversations[existingIndex] = freshConvo;
                }
                else
                {
                    // Yeni ekleme
                    Conversations.Add(freshConvo);
                    _conversationIds.Add(freshConvo.ConversationId);
                }
            }

            // 3️⃣ Sıralama
            SortConversationsInPlace();
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
                var result = await _messagingService.DeleteConversationAsync(conversation.ConversationId, _currentUser.UserId);

                if (!result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
                }
                // ✅ Real-time listener otomatik güncelleyecek
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
        }

        public void Dispose()
        {
            Console.WriteLine("🧹 MessagesViewModel dispose ediliyor...");
            _conversationsSubscription?.Dispose();
            _conversationsSubscription = null;
            _conversationIds.Clear();
            _isInitialized = false;
        }
    }
}