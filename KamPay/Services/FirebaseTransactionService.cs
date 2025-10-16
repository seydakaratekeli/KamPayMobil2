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
        private readonly INotificationService _notificationService;
        private readonly IProductService _productService;
        private readonly IQRCodeService _qrCodeService;

        public FirebaseTransactionService(
          INotificationService notificationService,
          IProductService productService,
          IQRCodeService qrCodeService) // <-- Buraya eklendi
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
            _productService = productService;
            _qrCodeService = qrCodeService; // <-- Gelen servis de�i�kene atan�yor
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

                // Sat�c�ya bildirim g�nder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = product.UserId,
                    Type = NotificationType.NewOffer,
                    Title = "Yeni Bir Teklifin Var!",
                    Message = $"{buyer.FullName}, '{product.Title}' �r�n�n i�in bir istek g�nderdi.",
                    ActionUrl = nameof(Views.OffersPage)
                });

                return ServiceResult<Transaction>.SuccessResult(transaction, "�ste�iniz ba�ar�yla g�nderildi.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("�stek olu�turulamad�.", ex.Message);
            }
        }

        public async Task<ServiceResult<Transaction>> CreateTradeOfferAsync(Product product, string offeredProductId, string message, User buyer)
        {
            try
            {
                // Teklif edilen �r�n�n bilgilerini al
                var offeredProduct = await _firebaseClient
                    .Child(Constants.ProductsCollection)
                    .Child(offeredProductId)
                    .OnceSingleAsync<Product>();

                if (offeredProduct == null)
                {
                    return ServiceResult<Transaction>.FailureResult("Teklif edilen �r�n bulunamad�.");
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

                // Sat�c�ya bildirim g�nder
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = product.UserId,
                    Type = NotificationType.NewOffer,
                    Title = "Yeni Bir Takas Teklifin Var!",
                    Message = $"{buyer.FullName}, '{product.Title}' �r�n�n i�in '{offeredProduct.Title}' �r�n�n� teklif etti.",
                    ActionUrl = nameof(Views.OffersPage)
                });
                return ServiceResult<Transaction>.SuccessResult(transaction, "Takas teklifiniz ba�ar�yla g�nderildi.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("Teklif olu�turulamad�.", ex.Message);
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
                return ServiceResult<List<Transaction>>.FailureResult("Gelen teklifler al�namad�.", ex.Message);
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
                return ServiceResult<List<Transaction>>.FailureResult("G�nderilen teklifler al�namad�.", ex.Message);
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
                    return ServiceResult<Transaction>.FailureResult("��lem bulunamad�.");
                }

                if (transaction.Status != TransactionStatus.Pending)
                {
                    return ServiceResult<Transaction>.SuccessResult(transaction, "Bu teklif zaten yan�tlanm��.");
                }

                transaction.Status = accept ? TransactionStatus.Accepted : TransactionStatus.Rejected;
                transaction.UpdatedAt = DateTime.UtcNow;

                await transactionNode.PutAsync(transaction);

                // Al�c�ya bildirim g�nder (mevcut kod)
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.BuyerId,
                    Type = accept ? NotificationType.OfferAccepted : NotificationType.OfferRejected,
                    Title = accept ? "Teklifin Kabul Edildi!" : "Teklifin Reddedildi",
                    Message = $"'{transaction.SellerName}', '{transaction.ProductTitle}' �r�n� i�in yapt���n teklifi {(accept ? "kabul etti." : "reddetti.")}",
                    ActionUrl = nameof(Views.OffersPage)
                });

                if (accept)
                {
                    // 1. �r�nleri rezerve et
                    await _productService.MarkAsReservedAsync(transaction.ProductId, true);
                    if (transaction.Type == ProductType.Takas && !string.IsNullOrEmpty(transaction.OfferedProductId))
                    {
                        await _productService.MarkAsReservedAsync(transaction.OfferedProductId, true);
                    }

                    // 2. QR Kodlar� Olu�tur (D�ZELT�LM�� KISIM)

                    // HATALI SATIRI S�L�YORUZ:
                    // var qrCodeService = new FirebaseQRCodeService(); 

                    // YER�NE DO�RU OLANI, YAN� ENJEKTE ED�LEN� KULLANIYORUZ:
                    await _qrCodeService.GenerateDeliveryQRCodeAsync(transaction.TransactionId, transaction.ProductId, transaction.ProductTitle, transaction.SellerId, transaction.BuyerId);

                    // E�er takas ise, al�c�n�n �r�n� i�in de QR kod olu�tur
                    if (transaction.Type == ProductType.Takas && !string.IsNullOrEmpty(transaction.OfferedProductId))
                    {
                        await _qrCodeService.GenerateDeliveryQRCodeAsync(transaction.TransactionId, transaction.OfferedProductId, transaction.OfferedProductTitle, transaction.BuyerId, transaction.SellerId);
                    }
                }
                return ServiceResult<Transaction>.SuccessResult(transaction, "Teklif yan�tland�.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("��lem s�ras�nda hata olu�tu.", ex.Message);
            }
        }

    }
}