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
        private readonly IUserProfileService _userProfileService; // YEN� SERV�S

        // Constructor'� INotificationService alacak �ekilde g�ncelleyin
        public FirebaseServiceSharingService(INotificationService notificationService, IUserProfileService userProfileService)
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
            _userProfileService = userProfileService; // Ata
        }

        // ... CreateServiceOfferAsync ve GetServiceOffersAsync metotlar� ayn� kalacak ...
        public async Task<ServiceResult<ServiceOffer>> CreateServiceOfferAsync(ServiceOffer offer)
        {
            try
            {
                await _firebaseClient
                    .Child(Constants.ServiceOffersCollection)
                    .Child(offer.ServiceId)
                    .PutAsync(offer);

                return ServiceResult<ServiceOffer>.SuccessResult(offer, "Hizmet payla��ld�!");
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

        // --- YEN� IMPLEMENTE ED�LEN METOTLAR ---

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
                    TimeCreditValue = offer.TimeCredits // YEN�: Kredi de�erini kaydet
                };

                await _firebaseClient
                    .Child(Constants.ServiceRequestsCollection)
                    .Child(request.RequestId)
                    .PutAsync(request);

                // Hizmeti sunan ki�iye bildirim g�nder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = offer.ProviderId,
                    Type = NotificationType.NewOffer, // Bu tipi genel teklifler i�in kullanabiliriz
                    Title = "Yeni Hizmet Talebi",
                    Message = $"{requester.FullName}, '{offer.Title}' hizmetin i�in bir talep g�nderdi.",
                    ActionUrl = "///ProfilePage" // �imdilik profile y�nlendirelim
                });

                return ServiceResult<ServiceRequest>.SuccessResult(request, "Talebiniz ba�ar�yla g�nderildi.");
            }
            catch (Exception ex)
            {
                return ServiceResult<ServiceRequest>.FailureResult("Talep g�nderilirken hata olu�tu.", ex.Message);
            }
        }
        // YEN� METODU IMPLEMENTE ED�N
        public async Task<ServiceResult<bool>> CompleteRequestAsync(string requestId, string currentUserId)
        {
            try
            {
                var requestNode = _firebaseClient.Child(Constants.ServiceRequestsCollection).Child(requestId);
                var request = await requestNode.OnceSingleAsync<ServiceRequest>();

                if (request == null) return ServiceResult<bool>.FailureResult("Talep bulunamad�.");

                // Sadece hizmeti talep eden ki�i tamamland� olarak i�aretleyebilir
                if (request.RequesterId != currentUserId)
                    return ServiceResult<bool>.FailureResult("Bu i�lemi yapmaya yetkiniz yok.");

                if (request.Status != ServiceRequestStatus.Accepted)
                    return ServiceResult<bool>.FailureResult("Bu talep hen�z onaylanmam�� veya zaten tamamlanm��.");

                // 1. Kredi transferini yap
                var transferResult = await _userProfileService.TransferTimeCreditsAsync(
                    request.RequesterId,
                    request.ProviderId,
                    request.TimeCreditValue,
                    $"Hizmet tamamland�: {request.ServiceTitle}"
                );

                if (!transferResult.Success)
                {
                    return ServiceResult<bool>.FailureResult($"Kredi transferi ba�ar�s�z: {transferResult.Message}");
                }

                // 2. Talebin durumunu g�ncelle
                request.Status = ServiceRequestStatus.Completed;
                await requestNode.PutAsync(request);

                // 3. Hizmeti sunan ki�iye bildirim g�nder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = request.ProviderId,
                    Title = "Hizmet Tamamland� ve Kredi Kazand�n!",
                    Message = $"{request.RequesterName}, '{request.ServiceTitle}' hizmetini tamamland� olarak i�aretledi. Hesab�na {request.TimeCreditValue} saat kredi eklendi."
                });

                return ServiceResult<bool>.SuccessResult(true, "Hizmet ba�ar�yla tamamland�.");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("��lem s�ras�nda hata olu�tu.", ex.Message);
            }
        }

        // KamPay/Services/FirebaseServiceSharingService.cs

        public async Task<ServiceResult<(List<ServiceRequest> Incoming, List<ServiceRequest> Outgoing)>> GetMyServiceRequestsAsync(string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    return ServiceResult<(List<ServiceRequest>, List<ServiceRequest>)>.FailureResult("Kullan�c� ID'si bulunamad�.");
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

                // HATA 2 ve 3 D�ZELTMES�: 'CreatedAt' yerine 'RequestedAt' kullan�l�yor.
                var incoming = incomingRequestsTask.Result
                    .Select(item => { item.Object.RequestId = item.Key; return item.Object; })
                    .OrderByDescending(r => r.RequestedAt)
                    .ToList();

                var outgoing = outgoingRequestsTask.Result
                    .Select(item => { item.Object.RequestId = item.Key; return item.Object; })
                    .OrderByDescending(r => r.RequestedAt)
                    .ToList();

                // HATA 1 D�ZELTMES�: Geri d�nd�r�len Tuple'a do�ru isimler veriliyor.
                return ServiceResult<(List<ServiceRequest>, List<ServiceRequest>)>.SuccessResult((Incoming: incoming, Outgoing: outgoing));
            }
            catch (Exception ex)
            {
                return ServiceResult<(List<ServiceRequest>, List<ServiceRequest>)>.FailureResult("Talepler getirilirken bir hata olu�tu.", ex.Message);
            }
        }

        // Bu metot �u an kullan�lm�yor ama ileride talepleri yan�tlarken gerekecek.
        public async Task<ServiceResult<bool>> RespondToRequestAsync(string requestId, bool accept)
        {
            try
            {
                var requestNode = _firebaseClient.Child(Constants.ServiceRequestsCollection).Child(requestId);
                var request = await requestNode.OnceSingleAsync<ServiceRequest>();

                if (request == null)
                {
                    return ServiceResult<bool>.FailureResult("Talep bulunamad�.");
                }

                request.Status = accept ? ServiceRequestStatus.Accepted : ServiceRequestStatus.Declined;
                await requestNode.PutAsync(request);

                // Talebi g�nderen ki�iye bildirim g�nder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = request.RequesterId,
                    Type = accept ? NotificationType.OfferAccepted : NotificationType.OfferRejected,
                    Title = accept ? "Hizmet Talebin Onayland�!" : "Hizmet Talebin Reddedildi",
                    Message = $"'{request.ServiceTitle}' hizmeti i�in talebin {(accept ? "kabul edildi." : "reddedildi.")}",
                    ActionUrl = "///ServiceSharingPage"
                });

                return ServiceResult<bool>.SuccessResult(true, "Talep yan�tland�.");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("��lem s�ras�nda hata olu�tu.", ex.Message);
            }
        }
    }
}