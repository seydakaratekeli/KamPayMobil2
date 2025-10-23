using System.Collections.Generic;
using System.Threading.Tasks;
using KamPay.Models;

namespace KamPay.Services
{
    public interface ITransactionService
    {
        // Bir takas teklifi olu�turur
        Task<ServiceResult<Transaction>> CreateTradeOfferAsync(Product product, string offeredProductId, string message, User buyer);

        // Bir ba��� veya sat�� i�in istek olu�turur
        Task<ServiceResult<Transaction>> CreateRequestAsync(Product product, User buyer);

        // Gelen bir teklife yan�t verir (Onayla/Reddet)
        Task<ServiceResult<Transaction>> RespondToOfferAsync(string transactionId, bool accept);

        // Kullan�c�n�n yapt��� teklifleri listeler
        Task<ServiceResult<List<Transaction>>> GetMyOffersAsync(string userId);

        // Kullan�c�ya gelen teklifleri listeler
        Task<ServiceResult<List<Transaction>>> GetIncomingOffersAsync(string userId);

        Task<ServiceResult<Transaction>> CompletePaymentAsync(string transactionId, string buyerId); // YEN� METOT

        Task<ServiceResult<Transaction>> ConfirmDonationAsync(string transactionId, string buyerId);

    }
}