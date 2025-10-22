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
    [QueryProperty(nameof(ConversationId), "conversationId")]
    public partial class ChatViewModel : ObservableObject, IDisposable
    {
        private readonly IMessagingService _messagingService;
        private readonly IAuthenticationService _authService;
        private readonly FirebaseClient _firebaseClient = new(Constants.FirebaseRealtimeDbUrl);
        private IDisposable _messagesSubscription;
        private bool _isListenerActive = false;
        private string _activeConversationId;

        [ObservableProperty]
        private string conversationId;

        [ObservableProperty]
        private Conversation conversation;

        [ObservableProperty]
        private string otherUserName;

        [ObservableProperty]
        private string otherUserPhoto;

        [ObservableProperty]
        private string messageText;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private bool isSending;

        public ObservableCollection<Message> Messages { get; } = new();

        private User _currentUser;

        public ChatViewModel(IMessagingService messagingService, IAuthenticationService authService)
        {
            _messagingService = messagingService;
            _authService = authService;
        }

        partial void OnConversationIdChanged(string value)
        {
            if (!string.IsNullOrEmpty(value) && value != _activeConversationId)
            {
                _ = LoadChatAsync();
            }
        }

        [RelayCommand]
        private async Task LoadChatAsync()
        {
            // 🔥 KONTROL: Aynı konuşma için tekrar yükleme yapma
            if (_isListenerActive && ConversationId == _activeConversationId)
            {
                Console.WriteLine("⚡ Listener zaten aktif, sadece okundu işaretleniyor.");
                await _messagingService.MarkMessagesAsReadAsync(ConversationId, _currentUser?.UserId);
                IsLoading = false;
                return;
            }

            try
            {
                IsLoading = true;

                // Eski konuşmadan geliyorsak mesajları temizle
                if (_activeConversationId != null && _activeConversationId != ConversationId)
                {
                    Messages.Clear();
                    _messagesSubscription?.Dispose();
                    _messagesSubscription = null;
                    _isListenerActive = false;
                }

                // Kullanıcı bilgisini al (sadece ilk kez)
                if (_currentUser == null)
                {
                    _currentUser = await _authService.GetCurrentUserAsync();
                    if (_currentUser == null)
                    {
                        await Application.Current.MainPage.DisplayAlert("Hata", "Giriş yapmış kullanıcı bulunamadı.", "Tamam");
                        await Shell.Current.GoToAsync("..");
                        return;
                    }
                }

                // Konuşma bilgilerini al (sadece ilk kez veya değiştiyse)
                if (Conversation == null || Conversation.ConversationId != ConversationId)
                {
                    var conversations = await _messagingService.GetUserConversationsAsync(_currentUser.UserId);
                    Conversation = conversations.Data?.FirstOrDefault(c => c.ConversationId == ConversationId);

                    if (Conversation != null)
                    {
                        OtherUserName = Conversation.GetOtherUserName(_currentUser.UserId);
                        OtherUserPhoto = Conversation.GetOtherUserPhotoUrl(_currentUser.UserId);
                    }
                }

                // 🔥 İLK YÜKLEME: Mevcut mesajları hızlıca çek (listener başlamadan önce)
                if (Messages.Count == 0)
                {
                    var messagesResult = await _messagingService.GetConversationMessagesAsync(ConversationId, 50);
                    if (messagesResult.Success && messagesResult.Data != null)
                    {
                        foreach (var msg in messagesResult.Data)
                        {
                            msg.IsSentByMe = msg.SenderId == _currentUser.UserId;
                            Messages.Add(msg);
                        }
                    }

                    // 🔥 İlk mesajlar yüklendikten HEMEN SONRA loading'i kapat
                    IsLoading = false;
                }

                // 🔥 Real-time listener'ı başlat (yeni mesajlar için)
                StartListeningToMessages();
                _activeConversationId = ConversationId;

                // Mesajları okundu işaretle
                await _messagingService.MarkMessagesAsReadAsync(ConversationId, _currentUser.UserId);
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
                Console.WriteLine($"❌ LoadChatAsync hatası: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        // 🔥 OPTIMIZE EDİLDİ: Listener sadece bir kez başlatılıyor
        private void StartListeningToMessages()
        {
            if (_isListenerActive)
            {
                Console.WriteLine("⚠️ Listener zaten aktif, yeniden başlatılmadı.");
                return;
            }

            Console.WriteLine($"🔥 Real-time listener başlatıldı: {ConversationId}");

            _messagesSubscription = _firebaseClient
                .Child(Constants.MessagesCollection)
                .Child(ConversationId)
                .AsObservable<Message>()
                .Where(e => e.Object != null && !e.Object.IsDeleted)
                .Subscribe(
                    e =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                var message = e.Object;
                                message.MessageId = e.Key;
                                message.IsSentByMe = message.SenderId == _currentUser.UserId;

                                var existingMessage = Messages.FirstOrDefault(m => m.MessageId == message.MessageId);

                                if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate)
                                {
                                    if (existingMessage != null)
                                    {
                                        // Mesaj güncellendi - mevcut pozisyonunu koru
                                        var index = Messages.IndexOf(existingMessage);
                                        Messages[index] = message;
                                    }
                                    else
                                    {
                                        // 🔥 YENİ MESAJ: Doğru pozisyona ekle (zaman sırasına göre)
                                        InsertMessageInOrder(message);

                                        // Eğer mesaj başkası tarafından gönderildiyse okundu işaretle
                                        if (!message.IsSentByMe)
                                        {
                                            _ = _messagingService.MarkMessagesAsReadAsync(ConversationId, _currentUser.UserId);
                                        }

                                        // Son mesaja kaydır (sadece yeni mesajsa)
                                        WeakReferenceMessenger.Default.Send(new ScrollToChatMessage(message));
                                    }
                                }
                                else if (e.EventType == Firebase.Database.Streaming.FirebaseEventType.Delete)
                                {
                                    if (existingMessage != null)
                                    {
                                        Messages.Remove(existingMessage);
                                    }
                                }

                                // İlk yükleme tamamlandığında loading'i kapat
                                IsLoading = false;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Message update hatası: {ex.Message}");
                            }
                        });
                    },
                    error =>
                    {
                        Console.WriteLine($"❌ Firebase message listener hatası: {error.Message}");
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            IsLoading = false;
                        });
                    });

            _isListenerActive = true;
        }

        // 🔥 YENİ: Mesajı zaman sırasına göre doğru pozisyona ekle
        private void InsertMessageInOrder(Message newMessage)
        {
            // Geçici mesaj varsa önce onu kaldır
            var tempMessage = Messages.FirstOrDefault(m => m.MessageId.StartsWith("temp_"));
            if (tempMessage != null && tempMessage.Content == newMessage.Content)
            {
                Messages.Remove(tempMessage);
            }

            // Liste boşsa veya son mesaj yeniden eskiyse direkt ekle
            if (Messages.Count == 0 || Messages.Last().SentAt <= newMessage.SentAt)
            {
                Messages.Add(newMessage);
                return;
            }

            // Doğru pozisyonu bul ve ekle (binary search benzeri)
            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                if (Messages[i].SentAt <= newMessage.SentAt)
                {
                    Messages.Insert(i + 1, newMessage);
                    return;
                }
            }

            // En eski mesajsa en başa ekle
            Messages.Insert(0, newMessage);
        }

        [RelayCommand]
        private async Task SendMessageAsync()
        {
            if (IsSending || string.IsNullOrWhiteSpace(MessageText) || _currentUser == null || Conversation == null)
            {
                return;
            }

            var messageContent = MessageText;
            MessageText = string.Empty;

            // 🔥 OPTIMISTIC UPDATE: Mesajı ANINDA UI'a ekle
            var tempMessage = new Message
            {
                MessageId = $"temp_{Guid.NewGuid()}", // Geçici ID (prefix ile ayırt ediyoruz)
                ConversationId = ConversationId,
                SenderId = _currentUser.UserId,
                SenderName = _currentUser.FullName,
                SenderPhotoUrl = _currentUser.ProfileImageUrl,
                ReceiverId = Conversation.GetOtherUserId(_currentUser.UserId),
                ReceiverName = Conversation.GetOtherUserName(_currentUser.UserId),
                ReceiverPhotoUrl = Conversation.GetOtherUserPhotoUrl(_currentUser.UserId),
                Content = messageContent.Trim(),
                Type = MessageType.Text,
                ProductId = Conversation.ProductId,
                ProductTitle = Conversation.ProductTitle,
                ProductThumbnail = Conversation.ProductThumbnail,
                IsSentByMe = true,
                SentAt = DateTime.UtcNow,
                IsDelivered = false, // 🔹 Henüz Firebase'e gönderilmedi
                IsRead = false
            };

            // UI'a anında ekle
            Messages.Add(tempMessage);
            WeakReferenceMessenger.Default.Send(new ScrollToChatMessage(tempMessage));

            try
            {
                IsSending = true;

                var request = new SendMessageRequest
                {
                    ReceiverId = Conversation.GetOtherUserId(_currentUser.UserId),
                    Content = messageContent.Trim(),
                    Type = MessageType.Text,
                    ProductId = Conversation.ProductId
                };

                if (string.IsNullOrEmpty(request.ReceiverId))
                {
                    // Hata: Geçici mesajı kaldır
                    Messages.Remove(tempMessage);
                    await Application.Current.MainPage.DisplayAlert("Hata", "Alıcı bilgisi bulunamadı.", "Tamam");
                    MessageText = messageContent;
                    return;
                }

                // Arka planda Firebase'e gönder
                var result = await _messagingService.SendMessageAsync(request, _currentUser);

                if (result.Success)
                {
                    // ✅ Başarılı: Geçici mesajı gerçek mesajla değiştir
                    var realMessage = result.Data;
                    realMessage.IsSentByMe = true;
                    realMessage.IsDelivered = true;

                    // Geçici mesajı bul ve kaldır
                    Messages.Remove(tempMessage);

                    // Gerçek mesajı doğru sıraya ekle (listener eklemeden önce)
                    // NOT: Listener zaten ekleyecek, o yüzden burada eklemeye gerek yok
                    // InsertMessageInOrder(realMessage); 
                }
                else
                {
                    // ❌ Hata: Geçici mesajı kaldır ve kullanıcıyı bilgilendir
                    Messages.Remove(tempMessage);
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message ?? "Mesaj gönderilemedi", "Tamam");
                    MessageText = messageContent;
                }
            }
            catch (Exception ex)
            {
                // ❌ İstisna: Geçici mesajı kaldır
                Messages.Remove(tempMessage);
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
                MessageText = messageContent;
                Console.WriteLine($"❌ SendMessage hatası: {ex.Message}");
            }
            finally
            {
                IsSending = false;
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            // Listener'ı temizle
            Dispose();
            await Shell.Current.GoToAsync("..");
        }

        public void Dispose()
        {
            Console.WriteLine("🧹 ChatViewModel dispose ediliyor...");
            _messagesSubscription?.Dispose();
            _messagesSubscription = null;
            _isListenerActive = false;
            _activeConversationId = null;
        }
    }
}