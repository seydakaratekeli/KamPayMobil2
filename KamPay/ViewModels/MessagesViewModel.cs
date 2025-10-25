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
        private bool isRefreshing = false; // 🔥 YENİ: RefreshView için

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

        // 🔥 OPTIMIZE: İlk yükleme sadece bir kez
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

                // 🔥 SADECE listener başlat (hem ilk yükleme hem real-time)
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

        // 🔥 OPTIMIZE: Verimsiz Clear() + Add() yerine akıllı güncelleme
        private void StartListeningForConversations()
        {
            if (_currentUser == null)
            {
                Console.WriteLine("⚠️ _currentUser null, listener başlatılamadı!");
                return;
            }

            Console.WriteLine("🔥 Real-time listener başlatılıyor...");

            _conversationsSubscription = _firebaseClient
                .Child(Constants.ConversationsCollection)
                .AsObservable<Conversation>()
                .Where(e => e.Object != null &&
                           e.Object.IsActive &&
                           (e.Object.User1Id == _currentUser.UserId || e.Object.User2Id == _currentUser.UserId))
                .Buffer(TimeSpan.FromMilliseconds(300)) // 🔥 YENİ: 300ms içindeki tüm değişiklikleri topla
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
                                Console.WriteLine($"❌ Conversation update hatası: {ex.Message}");
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

        // 🔥 YENİ: Batch işleme (Clear() yerine akıllı güncelleme)
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

                if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate)
                {
                    if (existingConvo != null)
                    {
                        // ✅ Mevcut öğeyi güncelle (Clear() YOK)
                        var index = Conversations.IndexOf(existingConvo);
                        Conversations[index] = conversation;
                    }
                    else
                    {
                        // ✅ Yeni öğe ekle
                        Conversations.Add(conversation);
                    }
                    hasChanges = true;
                }
                else if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.Delete)
                {
                    if (existingConvo != null)
                    {
                        Conversations.Remove(existingConvo);
                        hasChanges = true;
                    }
                }
            }

            // 🔥 SADECE değişiklik varsa sırala (Clear() YOK)
            if (hasChanges)
            {
                SortConversationsInPlace();
                UpdateUnreadCount();
                EmptyMessage = Conversations.Any() ? string.Empty : "Henüz mesajınız yok.";
            }
        }

        // 🔥 YENİ: In-place sorting (Clear() + Add() yerine)
        private void SortConversationsInPlace()
        {
            // ObservableCollection'ı sıralamak için geçici liste kullan
            var sorted = Conversations.OrderByDescending(c => c.LastMessageTime).ToList();

            // Sadece sıra değişenler için Move() kullan
            for (int i = 0; i < sorted.Count; i++)
            {
                var currentIndex = Conversations.IndexOf(sorted[i]);
                if (currentIndex != i)
                {
                    Conversations.Move(currentIndex, i);
                }
            }
        }

        // 🔥 YENİ: Okunmamış sayıyı güncelle
        private void UpdateUnreadCount()
        {
            UnreadCount = Conversations.Sum(c => c.UnreadCount);
            WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(UnreadCount > 0));
        }

        // 🔥 YENİ: Pull-to-Refresh Command
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
                    Conversations.Clear();

                    foreach (var convo in result.Data)
                    {
                        convo.OtherUserName = convo.GetOtherUserName(_currentUser.UserId);
                        convo.OtherUserPhotoUrl = convo.GetOtherUserPhotoUrl(_currentUser.UserId);
                        convo.UnreadCount = convo.GetUnreadCount(_currentUser.UserId);
                        Conversations.Add(convo);
                    }

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
            _isInitialized = false;
        }
    }
}