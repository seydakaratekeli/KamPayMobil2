using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KamPay.Services;

namespace KamPay.Services
{
    public class FirebaseTransactionService : ITransactionService
    {
        private readonly FirebaseClient _firebaseClient;
        private readonly INotificationService _notificationService; // BÝLDÝRÝM SERVÝSÝ
        private readonly IProductService _productService;

        public FirebaseTransactionService(INotificationService notificationService, IProductService productService)
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
            _productService = productService;
        }

        public async Task<ServiceResult<Transaction>> CreateRequestAsync(Product product, User buyer)
        {
            try
            {
                var transaction = new Transaction
                {
                    ProductId = product.ProductId,
                    ProductTitle = product.Title,
                    ProductThumbnailUrl = product.ThumbnailUrl,
                    Type = product.Type,
                    SellerId = product.UserId,
                    SellerName = product.UserName,
                    BuyerId = buyer.UserId,
                    BuyerName = buyer.FullName,
                    Status = TransactionStatus.Pending
                };

                await _firebaseClient
                    .Child(Constants.TransactionsCollection)
                    .Child(transaction.TransactionId)
                    .PutAsync(transaction);

                // Satýcýya bildirim gönder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = product.UserId,
                    Type = NotificationType.NewOffer,
                    Title = "Yeni Bir Teklifin Var!",
                    Message = $"{buyer.FullName}, '{product.Title}' ürünün için bir istek gönderdi.",
                    ActionUrl = nameof(Views.OffersPage)
                });

                return ServiceResult<Transaction>.SuccessResult(transaction, "Ýsteðiniz baþarýyla gönderildi.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("Ýstek oluþturulamadý.", ex.Message);
            }
        }

        public async Task<ServiceResult<Transaction>> CreateTradeOfferAsync(Product product, string offeredProductId, string message, User buyer)
        {
            try
            {
                // Teklif edilen ürünün bilgilerini al
                var offeredProduct = await _firebaseClient
                    .Child(Constants.ProductsCollection)
                    .Child(offeredProductId)
                    .OnceSingleAsync<Product>();

                if (offeredProduct == null)
                {
                    return ServiceResult<Transaction>.FailureResult("Teklif edilen ürün bulunamadý.");
                }

                var transaction = new Transaction
                {
                    ProductId = product.ProductId,
                    ProductTitle = product.Title,
                    ProductThumbnailUrl = product.ThumbnailUrl,
                    Type = ProductType.Takas,
                    SellerId = product.UserId,
                    SellerName = product.UserName,
                    BuyerId = buyer.UserId,
                    BuyerName = buyer.FullName,
                    Status = TransactionStatus.Pending,
                    OfferedProductId = offeredProductId,
                    OfferedProductTitle = offeredProduct.Title,
                    OfferMessage = message
                };

                await _firebaseClient
                    .Child(Constants.TransactionsCollection)
                    .Child(transaction.TransactionId)
                    .PutAsync(transaction);

                // Satýcýya bildirim gönder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = product.UserId,
                    Type = NotificationType.NewOffer,
                    Title = "Yeni Bir Takas Teklifin Var!",
                    Message = $"{buyer.FullName}, '{product.Title}' ürünün için '{offeredProduct.Title}' ürününü teklif etti.",
                    ActionUrl = nameof(Views.OffersPage)
                });
                return ServiceResult<Transaction>.SuccessResult(transaction, "Takas teklifiniz baþarýyla gönderildi.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("Teklif oluþturulamadý.", ex.Message);
            }
        }

        public async Task<ServiceResult<List<Transaction>>> GetIncomingOffersAsync(string userId)
        {
            try
            {
                var allTransactions = await _firebaseClient
                    .Child(Constants.TransactionsCollection)
                    .OrderBy("SellerId")
                    .EqualTo(userId)
                    .OnceAsync<Transaction>();

                var transactions = allTransactions.Select(t => t.Object).OrderByDescending(t => t.CreatedAt).ToList();
                return ServiceResult<List<Transaction>>.SuccessResult(transactions);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<Transaction>>.FailureResult("Gelen teklifler alýnamadý.", ex.Message);
            }
        }

        public async Task<ServiceResult<List<Transaction>>> GetMyOffersAsync(string userId)
        {
            try
            {
                var allTransactions = await _firebaseClient
                    .Child(Constants.TransactionsCollection)
                    .OrderBy("BuyerId")
                    .EqualTo(userId)
                    .OnceAsync<Transaction>();

                var transactions = allTransactions.Select(t => t.Object).OrderByDescending(t => t.CreatedAt).ToList();
                return ServiceResult<List<Transaction>>.SuccessResult(transactions);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<Transaction>>.FailureResult("Gönderilen teklifler alýnamadý.", ex.Message);
            }
        }

        public async Task<ServiceResult<Transaction>> RespondToOfferAsync(string transactionId, bool accept)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();

                if (transaction == null)
                {
                    return ServiceResult<Transaction>.FailureResult("Ýþlem bulunamadý.");
                }

                // Eðer iþlem zaten sonuçlandýysa tekrar iþlem yapmayý engelle
                if (transaction.Status == TransactionStatus.Accepted || transaction.Status == TransactionStatus.Completed)
                {
                    return ServiceResult<Transaction>.SuccessResult(transaction, "Bu teklif zaten yanýtlanmýþ.");
                }

                transaction.Status = accept ? TransactionStatus.Accepted : TransactionStatus.Rejected;
                transaction.UpdatedAt = DateTime.UtcNow;

                await transactionNode.PutAsync(transaction);

                // Alýcýya bildirim gönder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.BuyerId,
                    Type = accept ? NotificationType.OfferAccepted : NotificationType.OfferRejected,
                    Title = accept ? "Teklifin Kabul Edildi!" : "Teklifin Reddedildi",
                    Message = $"'{transaction.SellerName}', '{transaction.ProductTitle}' ürünü için yaptýðýn teklifi {(accept ? "kabul etti." : "reddetti.")}",
                    ActionUrl = nameof(Views.OffersPage)
                });

                // Eðer teklif KABUL EDÝLDÝYSE...
                if (accept)
                {
                    // 1. Talep edilen ürünü rezerve et
                    await _productService.MarkAsReservedAsync(transaction.ProductId, true);

                    // 2. EÐER BU BÝR TAKAS ÝSE, teklif edilen ürünü de rezerve et (YENÝ EKLENEN KISIM)
                    if (transaction.Type == ProductType.Takas && !string.IsNullOrEmpty(transaction.OfferedProductId))
                    {
                        await _productService.MarkAsReservedAsync(transaction.OfferedProductId, true);
                    }
                }

                return ServiceResult<Transaction>.SuccessResult(transaction, "Teklif yanýtlandý.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("Ýþlem sýrasýnda hata oluþtu.", ex.Message);
            }
        }
    }
    }