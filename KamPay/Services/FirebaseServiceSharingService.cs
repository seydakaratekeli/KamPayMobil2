using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Models;
using KamPay.Helpers;
using System; // Bu sat�r� ekleyin
using System.Collections.Generic; // Bu sat�r� ekleyin
using System.Linq; // Bu sat�r� ekleyin
using System.Threading.Tasks; // Bu sat�r� ekleyin

namespace KamPay.Services
{
    public class FirebaseServiceSharingService : IServiceSharingService
    {
        private readonly FirebaseClient _firebaseClient;
        private readonly INotificationService _notificationService; // Bildirim servisini ekleyin

        // Constructor'� INotificationService alacak �ekilde g�ncelleyin
        public FirebaseServiceSharingService(INotificationService notificationService)
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
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
                    Message = message
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

        // Bu metot �u an kullan�lm�yor ama ileride "Taleplerim" sayfas� i�in gerekecek.
        public async Task<ServiceResult<List<ServiceRequest>>> GetMyServiceRequestsAsync(string userId)
        {
            try
            {
                var requests = await _firebaseClient
                    .Child(Constants.ServiceRequestsCollection)
                    .OrderBy("ProviderId")
                    .EqualTo(userId)
                    .OnceAsync<ServiceRequest>();

                var list = requests.Select(r => r.Object).OrderByDescending(r => r.RequestedAt).ToList();
                return ServiceResult<List<ServiceRequest>>.SuccessResult(list);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<ServiceRequest>>.FailureResult("Talepler al�namad�.", ex.Message);
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