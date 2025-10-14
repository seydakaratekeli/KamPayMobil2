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

namespace KamPay.ViewModels
{

    // Chat (Sohbet) ViewModel
    [QueryProperty(nameof(ConversationId), "conversationId")]

    public partial class ChatViewModel : ObservableObject
    {
        private readonly IMessagingService _messagingService;
        private readonly IAuthenticationService _authService;

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

        // Bu Action, View'a (ChatPage.xaml.cs) son mesaja kaydýrmasý için sinyal gönderecek.
      //  public Action<Message> OnMessageSent { get; set; }

        private User _currentUser;

        public ChatViewModel(IMessagingService messagingService, IAuthenticationService authService)
        {
            _messagingService = messagingService;
            _authService = authService;
        }

        partial void OnConversationIdChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _ = LoadChatAsync();
            }
        }

        [RelayCommand]
        private async Task LoadChatAsync()
        {
            try
            {
                IsLoading = true;
                Messages.Clear();

                _currentUser = await _authService.GetCurrentUserAsync();
                if (_currentUser == null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Giriþ yapmýþ kullanýcý bulunamadý.", "Tamam");
                    await Shell.Current.GoToAsync("..");
                    return;
                }

                // Konuþma bilgilerini al
                var conversations = await _messagingService.GetUserConversationsAsync(_currentUser.UserId);
                Conversation = conversations.Data?.FirstOrDefault(c => c.ConversationId == ConversationId);

                if (Conversation != null)
                {
                    OtherUserName = Conversation.GetOtherUserName(_currentUser.UserId);
                    OtherUserPhoto = Conversation.GetOtherUserPhotoUrl(_currentUser.UserId);
                }

                // Mesajlarý yükle
                var messagesResult = await _messagingService.GetConversationMessagesAsync(ConversationId);

                if (messagesResult.Success && messagesResult.Data != null)
                {
                    Messages.Clear();
                    foreach (var msg in messagesResult.Data)
                    {
                        //  Her mesaj için IsSentByMe özelliðini ayarlýyoruz
                        msg.IsSentByMe = msg.SenderId == _currentUser.UserId;
                        Messages.Add(msg);
                    }
                }

                // Mesajlarý okundu iþaretle
                await _messagingService.MarkMessagesAsReadAsync(ConversationId, _currentUser.UserId);
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task SendMessageAsync()
        {
            // --- KONTROL 1: Gerekli bilgiler yüklenmiþ mi veya mesaj boþ mu? ---
            if (IsSending || string.IsNullOrWhiteSpace(MessageText) || _currentUser == null || Conversation == null)
            {
                // Eðer kullanýcý veya sohbet bilgisi henüz yüklenmediyse veya kutu boþsa hiçbir þey yapma.
                return;
            }

            // 2. Mesaj içeriðini geçici bir deðiþkene kaydet
            var messageContent = MessageText;

            // 3. UI'daki metin kutusunu ANINDA temizle (kullanýcý deneyimi için)
            MessageText = string.Empty;

            try
            {
                IsSending = true;
               

                var request = new SendMessageRequest
                {
                    ReceiverId = Conversation.GetOtherUserId(_currentUser.UserId),
                    Content = messageContent.Trim(), // Geçici deðiþkenden oku
                    Type = MessageType.Text,
                    ProductId = Conversation.ProductId // Konuþmadan ürün ID'sini al

                };
                // Servise göndermeden önce son bir kontrol yapalým
                if (string.IsNullOrEmpty(request.ReceiverId) || _currentUser == null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Alýcý veya gönderen bilgisi bulunamadý.", "Tamam");
                    MessageText = messageContent; // Mesajý geri yükle
                    IsSending = false;
                    return;
                }

                var result = await _messagingService.SendMessageAsync(request, _currentUser);

                if (result.Success)
                {
                    //  Yeni gönderilen mesajýn da IsSentByMe özelliðini ayarlýyoruz
                    var sentMessage = result.Data;
                    sentMessage.IsSentByMe = true;
                    Messages.Add(sentMessage);

                    // View'a "Mesaj gönderildi, son öðeye kaydýr" sinyalini gönder.
                    WeakReferenceMessenger.Default.Send(new ScrollToChatMessage(sentMessage));
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", result.Message, "Tamam");
                    MessageText = messageContent; // Hata durumunda yazýyý geri getir
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", ex.Message, "Tamam");
            }
            finally
            {
                IsSending = false;
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}