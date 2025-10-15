using KamPay.Models;

namespace KamPay.Services;

public interface IServiceSharingService
{
    Task<ServiceResult<ServiceOffer>> CreateServiceOfferAsync(ServiceOffer offer);
    Task<ServiceResult<List<ServiceOffer>>> GetServiceOffersAsync(ServiceCategory? category = null);
    Task<ServiceResult<ServiceRequest>> RequestServiceAsync(string serviceId, User requester, string message);
    Task<ServiceResult<bool>> RespondToRequestAsync(string requestId, bool accept);
}