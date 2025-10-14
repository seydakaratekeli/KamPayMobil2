using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;
using KamPay.Views;
using CommunityToolkit.Mvvm.Messaging; // Eklendi
using KamPay.ViewModels; // Eklendi

namespace KamPay.Services
{

    public class FirebaseMessagingService : IMessagingService
    {
        private readonly FirebaseClient _firebaseClient;
        //  Bildirim servisini kullanmak için
        private readonly INotificationService _notificationService;

        //  Artık INotificationService'i de alıyor
        public FirebaseMessagingService(INotificationService notificationService)
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
        }

        // Bu yeni metodu ekleyin
        private async Task CheckAndBroadcastUnreadMessageStatus(string userId)
        {
            var result = await GetTotalUnreadMessageCountAsync(userId);
            bool hasUnread = result.Success && result.Data > 0;
            WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(hasUnread));
        }

        // ===== BU METOT TAMAMEN GÜNCELLENDİ =====
        public async Task<ServiceResult<Message>> SendMessageAsync(SendMessageRequest request, User sender)
        {
            try
            {
                // Adım 1: Gerekli bilgilerin kontrolü
                if (request == null || sender == null || string.IsNullOrEmpty(request.ReceiverId) || string.IsNullOrEmpty(request.Content))
                {
                    return ServiceResult<Message>.FailureResult("Geçersiz istek: Gönderen, alıcı veya mesaj içeriği boş olamaz.");
                }

                // Adım 2: Konuşmayı al veya oluştur
                var conversationResult = await GetOrCreateConversationAsync(sender.UserId, request.ReceiverId, request.ProductId);
                if (!conversationResult.Success || conversationResult.Data == null)
                {
                    return ServiceResult<Message>.FailureResult("Konuşma oluşturulamadı veya bulunamadı.");
                }
                var conversation = conversationResult.Data;
                // ===== YENİ EKLENDİ: Alıcı bilgilerini çekme =====
                var receiver = await _firebaseClient
                    .Child(Constants.UsersCollection)
                    .Child(request.ReceiverId)
                    .OnceSingleAsync<User>();

                if (receiver == null)
                {
                    return ServiceResult<Message>.FailureResult("Alıcı kullanıcı bulunamadı.");
                }

                // Adım 3: Mesaj nesnesini oluştur
                var message = new Message
                {
                    ConversationId = conversation.ConversationId,
                    SenderId = sender.UserId,
                    SenderName = sender.FullName,
                    ReceiverId = request.ReceiverId,
                    Content = request.Content,
                    Type = request.Type,
                    ProductId = request.ProductId,
                    // ===== YENİ EKLENDİ: Alıcı bilgilerini atama =====
                    ReceiverName = receiver.FullName,
                    ReceiverPhotoUrl = receiver.ProfileImageUrl,
                    // ============================================

                 

                };

                // Adım 4: Eğer mesaj bir ürünle ilgiliyse, ürün bilgilerini mesaja ekle
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

                // Adım 5: Mesajı veritabanına kaydet
                await _firebaseClient
                    .Child(Constants.MessagesCollection)
                    .Child(conversation.ConversationId)
                    .Child(message.MessageId)
                    .PutAsync(message);

                // Adım 6: Konuşma nesnesini son mesaj bilgileriyle güncelle
                conversation.LastMessage = message.Type == MessageType.Text ? message.Content : "📷 Medya";
                conversation.LastMessageTime = DateTime.UtcNow;
                conversation.LastMessageSenderId = sender.UserId;
                conversation.UpdatedAt = DateTime.UtcNow;

                // Okunmamış mesaj sayısını artır
                if (conversation.User1Id == request.ReceiverId)
                    conversation.UnreadCountUser1++;
                else
                    conversation.UnreadCountUser2++;

                await _firebaseClient
                    .Child(Constants.ConversationsCollection)
                    .Child(conversation.ConversationId)
                    .PutAsync(conversation);

                // YENİ: Genel bildirim yerine mesaj rozeti için mesaj yayınla
               // WeakReferenceMessenger.Default.Send(new UnreadMessageStatusMessage(true));

               /* // Adım 7: Alıcıya bildirim gönder
                var notification = new Notification
                {
                    UserId = request.ReceiverId,
                    Type = NotificationType.NewMessage,
                    Title = $"Yeni Mesaj: {sender.FullName}",
                    Message = message.Content,
                    RelatedEntityId = conversation.ConversationId,
                    RelatedEntityType = "Conversation",
                    ActionUrl = $"{nameof(ChatPage)}?conversationId={conversation.ConversationId}"
                };*/
               // await _notificationService.CreateNotificationAsync(notification);

                return ServiceResult<Message>.SuccessResult(message, "Mesaj başarıyla gönderildi.");
            }
            catch (Exception ex)
            {
                // Hatanın detayını loglamak veya görmek için
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
                // Mevcut konuşmayı ara
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

                // Yeni konuşma oluştur
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

                // Ürün bilgisi varsa ekle
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
                // YENİ: Diğer konuşmalarda hala okunmamış mesaj var mı diye kontrol et
                await CheckAndBroadcastUnreadMessageStatus(readerUserId);

                if (conversation == null) return ServiceResult<bool>.FailureResult("Konuşma bulunamadı.");

                // Hangi kullanıcının okunmamış sayacının sıfırlanacağını belirle
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

                // Sadece pasif yap, tamamen silme
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
    }
}
