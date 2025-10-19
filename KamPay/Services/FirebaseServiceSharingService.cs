using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Models;
using KamPay.Helpers;
using System; 
using System.Collections.Generic; 
using System.Linq; 
using System.Threading.Tasks; 

namespace KamPay.Services
{
    public class FirebaseServiceSharingService : IServiceSharingService
    {
        private readonly FirebaseClient _firebaseClient;
        private readonly INotificationService _notificationService; // Bildirim servisini ekleyin
        private readonly IUserProfileService _userProfileService; // YENÝ SERVÝS

        // Constructor'ý INotificationService alacak þekilde güncelleyin
        public FirebaseServiceSharingService(INotificationService notificationService, IUserProfileService userProfileService)
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
            _userProfileService = userProfileService; // Ata
        }

        // ... CreateServiceOfferAsync ve GetServiceOffersAsync metotlarý ayný kalacak ...
        public async Task<ServiceResult<ServiceOffer>> CreateServiceOfferAsync(ServiceOffer offer)
        {
            try
            {
                await _firebaseClient
                    .Child(Constants.ServiceOffersCollection)
                    .Child(offer.ServiceId)
                    .PutAsync(offer);

                return ServiceResult<ServiceOffer>.SuccessResult(offer, "Hizmet paylaþýldý!");
            }
            catch (Exception ex)
            {
                return ServiceResult<ServiceOffer>.FailureResult("Hata", ex.Message);
            }
        }

        public async Task<ServiceResult<List<ServiceOffer>>> GetServiceOffersAsync(ServiceCategory? category = null)
        {
            try
            {
                var allOffers = await _firebaseClient
                    .Child(Constants.ServiceOffersCollection)
                    .OnceAsync<ServiceOffer>();

                var offers = allOffers
                    .Select(o => o.Object)
                    .Where(o => o.IsAvailable && (!category.HasValue || o.Category == category.Value))
                    .OrderByDescending(o => o.CreatedAt)
                    .ToList();

                return ServiceResult<List<ServiceOffer>>.SuccessResult(offers);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<ServiceOffer>>.FailureResult("Hata", ex.Message);
            }
        }

        // --- YENÝ IMPLEMENTE EDÝLEN METOTLAR ---

        public async Task<ServiceResult<ServiceRequest>> RequestServiceAsync(ServiceOffer offer, User requester, string message)
        {
            try
            {
                var request = new ServiceRequest
                {
                    ServiceId = offer.ServiceId,
                    ServiceTitle = offer.Title,
                    ProviderId = offer.ProviderId,
                    RequesterId = requester.UserId,
                    RequesterName = requester.FullName,
                    Message = message,
                    TimeCreditValue = offer.TimeCredits // YENÝ: Kredi deðerini kaydet
                };

                await _firebaseClient
                    .Child(Constants.ServiceRequestsCollection)
                    .Child(request.RequestId)
                    .PutAsync(request);

                // Hizmeti sunan kiþiye bildirim gönder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = offer.ProviderId,
                    Type = NotificationType.NewOffer, // Bu tipi genel teklifler için kullanabiliriz
                    Title = "Yeni Hizmet Talebi",
                    Message = $"{requester.FullName}, '{offer.Title}' hizmetin için bir talep gönderdi.",
                    ActionUrl = "///ProfilePage" // Þimdilik profile yönlendirelim
                });

                return ServiceResult<ServiceRequest>.SuccessResult(request, "Talebiniz baþarýyla gönderildi.");
            }
            catch (Exception ex)
            {
                return ServiceResult<ServiceRequest>.FailureResult("Talep gönderilirken hata oluþtu.", ex.Message);
            }
        }
        // YENÝ METODU IMPLEMENTE EDÝN
        public async Task<ServiceResult<bool>> CompleteRequestAsync(string requestId, string currentUserId)
        {
            try
            {
                var requestNode = _firebaseClient.Child(Constants.ServiceRequestsCollection).Child(requestId);
                var request = await requestNode.OnceSingleAsync<ServiceRequest>();

                if (request == null) return ServiceResult<bool>.FailureResult("Talep bulunamadý.");

                // Sadece hizmeti talep eden kiþi tamamlandý olarak iþaretleyebilir
                if (request.RequesterId != currentUserId)
                    return ServiceResult<bool>.FailureResult("Bu iþlemi yapmaya yetkiniz yok.");

                if (request.Status != ServiceRequestStatus.Accepted)
                    return ServiceResult<bool>.FailureResult("Bu talep henüz onaylanmamýþ veya zaten tamamlanmýþ.");

                // 1. Kredi transferini yap
                var transferResult = await _userProfileService.TransferTimeCreditsAsync(
                    request.RequesterId,
                    request.ProviderId,
                    request.TimeCreditValue,
                    $"Hizmet tamamlandý: {request.ServiceTitle}"
                );

                if (!transferResult.Success)
                {
                    return ServiceResult<bool>.FailureResult($"Kredi transferi baþarýsýz: {transferResult.Message}");
                }

                // 2. Talebin durumunu güncelle
                request.Status = ServiceRequestStatus.Completed;
                await requestNode.PutAsync(request);

                // 3. Hizmeti sunan kiþiye bildirim gönder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = request.ProviderId,
                    Title = "Hizmet Tamamlandý ve Kredi Kazandýn!",
                    Message = $"{request.RequesterName}, '{request.ServiceTitle}' hizmetini tamamlandý olarak iþaretledi. Hesabýna {request.TimeCreditValue} saat kredi eklendi."
                });

                return ServiceResult<bool>.SuccessResult(true, "Hizmet baþarýyla tamamlandý.");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Ýþlem sýrasýnda hata oluþtu.", ex.Message);
            }
        }

        // KamPay/Services/FirebaseServiceSharingService.cs

        public async Task<ServiceResult<(List<ServiceRequest> Incoming, List<ServiceRequest> Outgoing)>> GetMyServiceRequestsAsync(string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    return ServiceResult<(List<ServiceRequest>, List<ServiceRequest>)>.FailureResult("Kullanýcý ID'si bulunamadý.");
                }

                var incomingRequestsTask = _firebaseClient
                    .Child(Constants.ServiceRequestsCollection)
                    .OrderBy("ProviderId")
                    .EqualTo(userId)
                    .OnceAsync<ServiceRequest>();

                var outgoingRequestsTask = _firebaseClient
                    .Child(Constants.ServiceRequestsCollection)
                    .OrderBy("RequesterId")
                    .EqualTo(userId)
                    .OnceAsync<ServiceRequest>();

                await Task.WhenAll(incomingRequestsTask, outgoingRequestsTask);

                // HATA 2 ve 3 DÜZELTMESÝ: 'CreatedAt' yerine 'RequestedAt' kullanýlýyor.
                var incoming = incomingRequestsTask.Result
                    .Select(item => { item.Object.RequestId = item.Key; return item.Object; })
                    .OrderByDescending(r => r.RequestedAt)
                    .ToList();

                var outgoing = outgoingRequestsTask.Result
                    .Select(item => { item.Object.RequestId = item.Key; return item.Object; })
                    .OrderByDescending(r => r.RequestedAt)
                    .ToList();

                // HATA 1 DÜZELTMESÝ: Geri döndürülen Tuple'a doðru isimler veriliyor.
                return ServiceResult<(List<ServiceRequest>, List<ServiceRequest>)>.SuccessResult((Incoming: incoming, Outgoing: outgoing));
            }
            catch (Exception ex)
            {
                return ServiceResult<(List<ServiceRequest>, List<ServiceRequest>)>.FailureResult("Talepler getirilirken bir hata oluþtu.", ex.Message);
            }
        }

        // Bu metot þu an kullanýlmýyor ama ileride talepleri yanýtlarken gerekecek.
        public async Task<ServiceResult<bool>> RespondToRequestAsync(string requestId, bool accept)
        {
            try
            {
                var requestNode = _firebaseClient.Child(Constants.ServiceRequestsCollection).Child(requestId);
                var request = await requestNode.OnceSingleAsync<ServiceRequest>();

                if (request == null)
                {
                    return ServiceResult<bool>.FailureResult("Talep bulunamadý.");
                }

                request.Status = accept ? ServiceRequestStatus.Accepted : ServiceRequestStatus.Declined;
                await requestNode.PutAsync(request);

                // Talebi gönderen kiþiye bildirim gönder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = request.RequesterId,
                    Type = accept ? NotificationType.OfferAccepted : NotificationType.OfferRejected,
                    Title = accept ? "Hizmet Talebin Onaylandý!" : "Hizmet Talebin Reddedildi",
                    Message = $"'{request.ServiceTitle}' hizmeti için talebin {(accept ? "kabul edildi." : "reddedildi.")}",
                    ActionUrl = "///ServiceSharingPage"
                });

                return ServiceResult<bool>.SuccessResult(true, "Talep yanýtlandý.");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Ýþlem sýrasýnda hata oluþtu.", ex.Message);
            }
        }
    }
}