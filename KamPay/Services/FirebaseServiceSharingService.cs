using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Models;
using KamPay.Helpers;

namespace KamPay.Services
{
    public class FirebaseServiceSharingService : IServiceSharingService
    {
        private readonly FirebaseClient _firebaseClient;
        private const string ServiceOffersCollection = "service_offers";
        private const string ServiceRequestsCollection = "service_requests";

        public FirebaseServiceSharingService()
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
        }

        // Hizmet oluþturma 
        public async Task<ServiceResult<ServiceOffer>> CreateServiceOfferAsync(ServiceOffer offer)
        {
            try
            {
                await _firebaseClient
                    .Child(ServiceOffersCollection)
                    .Child(offer.ServiceId)
                    .PutAsync(offer);

                return ServiceResult<ServiceOffer>.SuccessResult(offer, "Hizmet paylaþýldý!");
            }
            catch (Exception ex)
            {
                return ServiceResult<ServiceOffer>.FailureResult("Hata", ex.Message);
            }
        }

        // === Hizmetleri listeleme ===
        public async Task<ServiceResult<List<ServiceOffer>>> GetServiceOffersAsync(ServiceCategory? category = null)
        {
            try
            {
                var allOffers = await _firebaseClient
                    .Child(ServiceOffersCollection)
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

        // === Hizmet talebi oluþturma ===
        public async Task<ServiceResult<ServiceRequest>> RequestServiceAsync(string serviceId, User requester, string message)
        {
            try
            {
                var request = new ServiceRequest
                {
                    ServiceId = serviceId,
                    RequesterId = requester.UserId,
                    RequesterName = requester.FullName,
                    Message = message
                };

                await _firebaseClient
                    .Child(ServiceRequestsCollection)
                    .Child(request.RequestId)
                    .PutAsync(request);

                return ServiceResult<ServiceRequest>.SuccessResult(request, "Talep gönderildi");
            }
            catch (Exception ex)
            {
                return ServiceResult<ServiceRequest>.FailureResult("Hata", ex.Message);
            }
        }

        // === Hizmet talebine yanýt verme ===
        public async Task<ServiceResult<bool>> RespondToRequestAsync(string requestId, bool accept)
        {
            try
            {
                var request = await _firebaseClient
                    .Child(ServiceRequestsCollection)
                    .Child(requestId)
                    .OnceSingleAsync<ServiceRequest>();

                if (request != null)
                {
                    request.Status = accept ? ServiceRequestStatus.Accepted : ServiceRequestStatus.Declined;

                    await _firebaseClient
                        .Child(ServiceRequestsCollection)
                        .Child(requestId)
                        .PutAsync(request);
                }

                return ServiceResult<bool>.SuccessResult(true);
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Hata", ex.Message);
            }
        }
    }
}
