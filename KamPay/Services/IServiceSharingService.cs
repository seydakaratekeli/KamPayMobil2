using KamPay.Models;
using System.Collections.Generic; // List i�in eklendi
using System.Threading.Tasks; // Bu sat�r� ekleyin

namespace KamPay.Services;

public interface IServiceSharingService
{
    Task<ServiceResult<ServiceOffer>> CreateServiceOfferAsync(ServiceOffer offer);
    Task<ServiceResult<List<ServiceOffer>>> GetServiceOffersAsync(ServiceCategory? category = null);

    // --- YEN� EKLENEN METOTLAR ---
    Task<ServiceResult<ServiceRequest>> RequestServiceAsync(ServiceOffer offer, User requester, string message);
    Task<ServiceResult<(List<ServiceRequest> Incoming, List<ServiceRequest> Outgoing)>> GetMyServiceRequestsAsync(string userId); Task<ServiceResult<bool>> RespondToRequestAsync(string requestId, bool accept);

    Task<ServiceResult<bool>> CompleteRequestAsync(string requestId, string currentUserId);

}