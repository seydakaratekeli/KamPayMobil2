using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;
using KamPay.Views;
using CommunityToolkit.Mvvm.Messaging;
using KamPay.ViewModels;

namespace KamPay.Services
{
    public class FirebaseMessagingService : IMessagingService
    {
        private readonly FirebaseClient _firebaseClient;
        private readonly INotificationService _notificationService;

        public FirebaseMessagingService(INotificationService notificationService)
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
        }

        private async Task CheckAndBroadcastUnreadMessageStatus(string userId)
        {
            var result = await GetTotalUnreadMessageCountAsync(userId);
            bool hasUnread = result.Success && result.Data > 0;
            WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(hasUnread));
        }

        public async Task<ServiceResult<Message>> SendMessageAsync(SendMessageRequest request, User sender)
        {
            try
            {
                if (request == null || sender == null || string.IsNullOrEmpty(request.ReceiverId) || string.IsNullOrEmpty(request.Content))
                {
                    return ServiceResult<Message>.FailureResult("Geçersiz istek: Gönderen, alıcı veya mesaj içeriği boş olamaz.");
                }

                var conversationResult = await GetOrCreateConversationAsync(sender.UserId, request.ReceiverId, request.ProductId);
                if (!conversationResult.Success || conversationResult.Data == null)
                {
                    return ServiceResult<Message>.FailureResult("Konuşma oluşturulamadı veya bulunamadı.");
                }
                var conversation = conversationResult.Data;

                var receiver = await _firebaseClient
                    .Child(Constants.UsersCollection)
                    .Child(request.ReceiverId)
                    .OnceSingleAsync<User>();

                if (receiver == null)
                {
                    return ServiceResult<Message>.FailureResult("Alıcı kullanıcı bulunamadı.");
                }

                var message = new Message
                {
                    ConversationId = conversation.ConversationId,
                    SenderId = sender.UserId,
                    SenderName = sender.FullName,
                    ReceiverId = request.ReceiverId,
                    Content = request.Content,
                    Type = request.Type,
                    ProductId = request.ProductId,
                    ReceiverName = receiver.FullName,
                    ReceiverPhotoUrl = receiver.ProfileImageUrl,
                };

                if (!string.IsNullOrEmpty(request.ProductId))
                {
                    var product = await _firebaseClient
                        .Child(Constants.ProductsCollection)
                        .Child(request.ProductId)
                        .OnceSingleAsync<Product>();

                    if (product != null)
                    {
                        message.ProductTitle = product.Title;
                        message.ProductThumbnail = product.ThumbnailUrl;
                    }
                }

                await _firebaseClient
                    .Child(Constants.MessagesCollection)
                    .Child(conversation.ConversationId)
                    .Child(message.MessageId)
                    .PutAsync(message);

                conversation.LastMessage = message.Type == MessageType.Text ? message.Content : "📷 Medya";
                conversation.LastMessageTime = DateTime.UtcNow;
                conversation.LastMessageSenderId = sender.UserId;
                conversation.UpdatedAt = DateTime.UtcNow;

                if (conversation.User1Id == request.ReceiverId)
                    conversation.UnreadCountUser1++;
                else
                    conversation.UnreadCountUser2++;

                await _firebaseClient
                    .Child(Constants.ConversationsCollection)
                    .Child(conversation.ConversationId)
                    .PutAsync(conversation);

                return ServiceResult<Message>.SuccessResult(message, "Mesaj başarıyla gönderildi.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SendMessageAsync Hata: {ex.Message}");
                return ServiceResult<Message>.FailureResult("Mesaj gönderilemedi. Bir hata oluştu.", ex.Message);
            }
        }

        public async Task<ServiceResult<List<Message>>> GetConversationMessagesAsync(string conversationId, int limit = 50)
        {
            try
            {
                var messagesRef = await _firebaseClient
                    .Child(Constants.MessagesCollection)
                    .Child(conversationId)
                    .OnceAsync<Message>();

                var messages = messagesRef
                    .Select(m => m.Object)
                    .Where(m => !m.IsDeleted)
                    .OrderByDescending(m => m.SentAt)
                    .Take(limit)
                    .Reverse()
                    .ToList();

                return ServiceResult<List<Message>>.SuccessResult(messages);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<Message>>.FailureResult("Mesajlar yüklenemedi", ex.Message);
            }
        }

        public async Task<ServiceResult<List<Conversation>>> GetUserConversationsAsync(string userId)
        {
            try
            {
                var allConversations = await _firebaseClient
                    .Child(Constants.ConversationsCollection)
                    .OnceAsync<Conversation>();

                var conversations = allConversations
                    .Select(c => c.Object)
                    .Where(c => c.IsActive && (c.User1Id == userId || c.User2Id == userId))
                    .OrderByDescending(c => c.LastMessageTime)
                    .ToList();

                return ServiceResult<List<Conversation>>.SuccessResult(conversations);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<Conversation>>.FailureResult("Konuşmalar yüklenemedi", ex.Message);
            }
        }

        public async Task<ServiceResult<Conversation>> GetOrCreateConversationAsync(string user1Id, string user2Id, string productId = null)
        {
            try
            {
                var allConversations = await _firebaseClient
                    .Child(Constants.ConversationsCollection)
                    .OnceAsync<Conversation>();

                var existing = allConversations
                    .Select(c => c.Object)
                    .FirstOrDefault(c =>
                        c.IsActive &&
                        ((c.User1Id == user1Id && c.User2Id == user2Id) ||
                         (c.User1Id == user2Id && c.User2Id == user1Id)));

                if (existing != null)
                {
                    return ServiceResult<Conversation>.SuccessResult(existing);
                }

                var user1 = await _firebaseClient
                    .Child(Constants.UsersCollection)
                    .Child(user1Id)
                    .OnceSingleAsync<User>();

                var user2 = await _firebaseClient
                    .Child(Constants.UsersCollection)
                    .Child(user2Id)
                    .OnceSingleAsync<User>();

                if (user1 == null || user2 == null)
                {
                    return ServiceResult<Conversation>.FailureResult("Kullanıcı bulunamadı");
                }

                var conversation = new Conversation
                {
                    User1Id = user1Id,
                    User1Name = user1.FullName,
                    User1PhotoUrl = user1.ProfileImageUrl,
                    User2Id = user2Id,
                    User2Name = user2.FullName,
                    User2PhotoUrl = user2.ProfileImageUrl,
                    LastMessage = "Konuşma başladı",
                    LastMessageTime = DateTime.UtcNow
                };

                if (!string.IsNullOrEmpty(productId))
                {
                    var product = await _firebaseClient
                        .Child(Constants.ProductsCollection)
                        .Child(productId)
                        .OnceSingleAsync<Product>();

                    if (product != null)
                    {
                        conversation.ProductId = productId;
                        conversation.ProductTitle = product.Title;
                        conversation.ProductThumbnail = product.ThumbnailUrl;
                    }
                }

                await _firebaseClient
                    .Child(Constants.ConversationsCollection)
                    .Child(conversation.ConversationId)
                    .PutAsync(conversation);

                return ServiceResult<Conversation>.SuccessResult(conversation);
            }
            catch (Exception ex)
            {
                return ServiceResult<Conversation>.FailureResult("Konuşma oluşturulamadı", ex.Message);
            }
        }

        public async Task<ServiceResult<bool>> MarkMessagesAsReadAsync(string conversationId, string readerUserId)
        {
            try
            {
                var conversation = await _firebaseClient
                    .Child(Constants.ConversationsCollection)
                    .Child(conversationId)
                    .OnceSingleAsync<Conversation>();

                await CheckAndBroadcastUnreadMessageStatus(readerUserId);

                if (conversation == null) return ServiceResult<bool>.FailureResult("Konuşma bulunamadı.");

                if (conversation.User1Id == readerUserId)
                {
                    conversation.UnreadCountUser1 = 0;
                }
                else if (conversation.User2Id == readerUserId)
                {
                    conversation.UnreadCountUser2 = 0;
                }

                await _firebaseClient
                    .Child(Constants.ConversationsCollection)
                    .Child(conversationId)
                    .PutAsync(conversation);

                return ServiceResult<bool>.SuccessResult(true);
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Mesajlar okundu olarak işaretlenemedi.", ex.Message);
            }
        }

        public async Task<ServiceResult<int>> GetTotalUnreadMessageCountAsync(string userId)
        {
            try
            {
                var conversationsResult = await GetUserConversationsAsync(userId);
                if (!conversationsResult.Success) return ServiceResult<int>.FailureResult("Okunmamış mesajlar sayılamadı.");

                int totalUnread = 0;
                foreach (var convo in conversationsResult.Data)
                {
                    if (convo.User1Id == userId)
                    {
                        totalUnread += convo.UnreadCountUser1;
                    }
                    else
                    {
                        totalUnread += convo.UnreadCountUser2;
                    }
                }
                return ServiceResult<int>.SuccessResult(totalUnread);
            }
            catch (Exception ex)
            {
                return ServiceResult<int>.FailureResult("Okunmamış mesajlar sayılamadı.", ex.Message);
            }
        }

        public async Task<ServiceResult<bool>> DeleteConversationAsync(string conversationId, string userId)
        {
            try
            {
                var conversation = await _firebaseClient
                    .Child(Constants.ConversationsCollection)
                    .Child(conversationId)
                    .OnceSingleAsync<Conversation>();

                if (conversation == null)
                {
                    return ServiceResult<bool>.FailureResult("Konuşma bulunamadı");
                }

                conversation.IsActive = false;

                await _firebaseClient
                    .Child(Constants.ConversationsCollection)
                    .Child(conversationId)
                    .PutAsync(conversation);

                return ServiceResult<bool>.SuccessResult(true, "Konuşma silindi");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Silme başarısız", ex.Message);
            }
        }

        // 🔥 YENİ: Konuşmaları real-time dinle
        public IDisposable SubscribeToConversations(string userId, Action<List<Conversation>> onConversationsChanged)
        {
            var observable = _firebaseClient
                .Child(Constants.ConversationsCollection)
                .AsObservable<Conversation>();

            return observable.Subscribe(changeEvent =>
            {
                try
                {
                    // Tüm aktif konuşmaları tekrar çek
                    Task.Run(async () =>
                    {
                        var result = await GetUserConversationsAsync(userId);
                        if (result.Success)
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                onConversationsChanged?.Invoke(result.Data);
                            });
                        }
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SubscribeToConversations Hata: {ex.Message}");
                }
            });
        }

        // 🔥 YENİ: Mesajları real-time dinle
        public IDisposable SubscribeToMessages(string conversationId, Action<List<Message>> onMessagesChanged)
        {
            var observable = _firebaseClient
                .Child(Constants.MessagesCollection)
                .Child(conversationId)
                .AsObservable<Message>();

            return observable.Subscribe(changeEvent =>
            {
                try
                {
                    Task.Run(async () =>
                    {
                        var result = await GetConversationMessagesAsync(conversationId);
                        if (result.Success)
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                onMessagesChanged?.Invoke(result.Data);
                            });
                        }
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SubscribeToMessages Hata: {ex.Message}");
                }
            });
        }
    }
}