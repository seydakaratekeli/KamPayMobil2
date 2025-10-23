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
        private readonly IUserProfileService _userProfileService; // Puan için eklendi

        public FirebaseTransactionService(
          INotificationService notificationService,
          IProductService productService,
          IQRCodeService qrCodeService,
          IUserProfileService userProfileService) // UserProfileService eklendi // <-- Buraya eklendi
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
            _productService = productService;
            _qrCodeService = qrCodeService; // <-- Gelen servis deðiþkene atanýyor
            _userProfileService = userProfileService; // Atama yapýldý
        }


        public async Task<ServiceResult<Transaction>> RespondToOfferAsync(string transactionId, bool accept)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();

                if (transaction == null) return ServiceResult<Transaction>.FailureResult("Ýþlem bulunamadý.");
                if (transaction.Status != TransactionStatus.Pending) return ServiceResult<Transaction>.SuccessResult(transaction, "Bu teklif zaten yanýtlanmýþ.");

                transaction.Status = accept ? TransactionStatus.Accepted : TransactionStatus.Rejected;
                transaction.UpdatedAt = DateTime.UtcNow;

                await transactionNode.PutAsync(transaction);

                // Alýcýya bildirim gönder (mevcut kod)
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.BuyerId,
                    Type = accept ? NotificationType.OfferAccepted : NotificationType.OfferRejected,
                    Title = accept ? "Teklifin Kabul Edildi!" : "Teklifin Reddedildi",
                    Message = $"'{transaction.SellerName}', '{transaction.ProductTitle}' ürünü için yaptýðýn teklifi {(accept ? "kabul etti. Þimdi ödemeyi tamamlayabilirsin." : "reddetti.")}",
                    ActionUrl = nameof(Views.OffersPage) // Giden Tekliflerim'e yönlendirsin
                });

                if (accept)
                {
                    // Ürünleri rezerve et
                    await _productService.MarkAsReservedAsync(transaction.ProductId, true);

                    if (transaction.Type == ProductType.Takas && !string.IsNullOrEmpty(transaction.OfferedProductId))
                    {
                        await _productService.MarkAsReservedAsync(transaction.OfferedProductId, true);
                    }

                    // --- QR KOD OLUÞTURMA KISMI (ÝSTEÐÝNÝZ ÜZERÝNE DEVRE DIÞI) ---
                    /*
                    await _qrCodeService.GenerateDeliveryQRCodeAsync(transaction.TransactionId, transaction.ProductId, transaction.ProductTitle, transaction.SellerId, transaction.BuyerId);
                    if (transaction.Type == ProductType.Takas && !string.IsNullOrEmpty(transaction.OfferedProductId))
                    {
                        await _qrCodeService.GenerateDeliveryQRCodeAsync(transaction.TransactionId, transaction.OfferedProductId, transaction.OfferedProductTitle, transaction.BuyerId, transaction.SellerId);
                    }
                    */
                    // --- --- ---
                }
                return ServiceResult<Transaction>.SuccessResult(transaction, "Teklif yanýtlandý.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("Ýþlem sýrasýnda hata oluþtu.", ex.Message);
            }
        }
        // --- YENÝ METOT: CompletePaymentAsync ---
        public async Task<ServiceResult<Transaction>> CompletePaymentAsync(string transactionId, string buyerId)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();

                // Kontroller
                if (transaction == null) return ServiceResult<Transaction>.FailureResult("Ýþlem bulunamadý.");
                if (transaction.BuyerId != buyerId) return ServiceResult<Transaction>.FailureResult("Bu iþlemi yapmaya yetkiniz yok.");
                if (transaction.Status != TransactionStatus.Accepted) return ServiceResult<Transaction>.FailureResult("Bu iþlem onaylanmamýþ veya zaten tamamlanmýþ.");
                if (transaction.PaymentStatus != PaymentStatus.Pending) return ServiceResult<Transaction>.FailureResult("Bu iþlemin ödemesi zaten yapýlmýþ veya baþarýsýz olmuþ.");
                if (transaction.Type != ProductType.Satis) return ServiceResult<Transaction>.FailureResult("Bu iþlem bir satýþ iþlemi deðil.");


                // Simülasyon: Ödeme baþarýlý kabul ediliyor.
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.UpdatedAt = DateTime.UtcNow;
                await transactionNode.PutAsync(transaction);

                // Ýþlemi tamamla (Internal metodu çaðýr)
                return await CompleteTransactionInternalAsync(transaction);

            }
            catch (Exception ex)
            {
                // Ödeme baþarýsýz olursa durumu güncelle (Opsiyonel ama iyi pratik)
                try
                {
                    var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                    var transaction = await transactionNode.OnceSingleAsync<Transaction>();
                    if (transaction != null && transaction.PaymentStatus == PaymentStatus.Pending)
                    {
                        transaction.PaymentStatus = PaymentStatus.Failed;
                        await transactionNode.PutAsync(transaction);
                    }
                }
                catch { /* Loglama yapýlabilir */ }

                return ServiceResult<Transaction>.FailureResult("Ödeme tamamlanýrken hata oluþtu.", ex.Message);
            }
        }

        // --- YENÝ PRIVATE METOT: CompleteTransactionInternalAsync ---
        private async Task<ServiceResult<Transaction>> CompleteTransactionInternalAsync(Transaction transaction)
        {
            try
            {
                // 1. Transaction durumunu 'Completed' yap
                transaction.Status = TransactionStatus.Completed;
                transaction.UpdatedAt = DateTime.UtcNow;
                await _firebaseClient.Child(Constants.TransactionsCollection).Child(transaction.TransactionId).PutAsync(transaction);

                // 2. Ürünü 'Satýldý' olarak iþaretle (Artýk rezerve deðil, tamamen satýldý)
                // Dikkat: MarkAsSoldAsync ürünü IsActive=false yapar ve listeden kaldýrýr.
                await _productService.MarkAsSoldAsync(transaction.ProductId);

                // 3. Bildirimleri gönder
                // Satýcýya:
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.SellerId,
                    Type = NotificationType.ProductSold,
                    Title = "Ürünün Satýldý!",
                    Message = $"'{transaction.ProductTitle}' ürünün '{transaction.BuyerName}' tarafýndan satýn alýndý ve iþlem tamamlandý.",
                    ActionUrl = nameof(Views.OffersPage) // Gelen Teklifler'e gidebilir
                });
                // Alýcýya:
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.BuyerId,
                    Type = NotificationType.ProductSold, // Ayný tip kullanýlabilir veya yeni tip eklenebilir
                    Title = "Satýn Alma Tamamlandý!",
                    Message = $"'{transaction.ProductTitle}' ürününü baþarýyla satýn aldýn.",
                    ActionUrl = nameof(Views.OffersPage) // Giden Teklifler'e gidebilir
                });

                // 4. Puanlarý ekle
                await _userProfileService.AddPointsForAction(transaction.SellerId, UserAction.CompleteTransaction);
                await _userProfileService.AddPointsForAction(transaction.BuyerId, UserAction.CompleteTransaction); // Alýcýya da puan eklenebilir.

                return ServiceResult<Transaction>.SuccessResult(transaction, "Ýþlem baþarýyla tamamlandý.");

            }
            catch (Exception ex)
            {
                // Hata durumunda iþlemi geri almak zor olabilir, bu yüzden loglama önemli.
                Console.WriteLine($"Hata - CompleteTransactionInternalAsync: {ex.Message}");
                // Belki transaction durumunu 'Failed' gibi bir þeye çekebiliriz?
                return ServiceResult<Transaction>.FailureResult("Ýþlem tamamlanýrken bir hata oluþtu.", ex.Message);
            }
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
                    Type = product.Type, // DÝKKAT: Product'tan gelen tipi kullanýyoruz
                    SellerId = product.UserId,
                    SellerName = product.UserName, // Product modelinde UserName olduðunu varsayýyorum
                    BuyerId = buyer.UserId,
                    BuyerName = buyer.FullName,
                    Status = TransactionStatus.Pending,
                    PaymentStatus = PaymentStatus.Pending // Baþlangýçta ödeme bekliyor
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
                    Title = product.Type == ProductType.Bagis ? "Yeni Baðýþ Talebi!" : "Yeni Satýþ Teklifi!",
                    Message = $"{buyer.FullName}, '{product.Title}' ürünün için bir {(product.Type == ProductType.Bagis ? "talep" : "teklif")} gönderdi.",
                    ActionUrl = nameof(Views.OffersPage) // Gelen Teklifler sayfasý
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
                var offeredProductResult = await _productService.GetProductByIdAsync(offeredProductId); // ProductService kullanmak daha iyi
                if (!offeredProductResult.Success || offeredProductResult.Data == null)
                {
                    return ServiceResult<Transaction>.FailureResult("Teklif edilen ürün bulunamadý.");
                }
                var offeredProduct = offeredProductResult.Data;


                var transaction = new Transaction
                {
                    ProductId = product.ProductId,
                    ProductTitle = product.Title,
                    ProductThumbnailUrl = product.ThumbnailUrl,
                    Type = ProductType.Takas, // Bu kesin Takas
                    SellerId = product.UserId,
                    SellerName = product.UserName,
                    BuyerId = buyer.UserId,
                    BuyerName = buyer.FullName,
                    Status = TransactionStatus.Pending,
                    OfferedProductId = offeredProductId,
                    OfferedProductTitle = offeredProduct.Title,
                    OfferMessage = message,
                    PaymentStatus = PaymentStatus.Pending // Takasta ödeme durumu her zaman Pending kalabilir veya kaldýrýlabilir.
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

        // FirebaseTransactionService.cs içine yeni metot
        public async Task<ServiceResult<Transaction>> ConfirmDonationAsync(string transactionId, string buyerId)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();

                // Kontroller
                if (transaction == null) return ServiceResult<Transaction>.FailureResult("Ýþlem bulunamadý.");
                if (transaction.BuyerId != buyerId) return ServiceResult<Transaction>.FailureResult("Bu iþlemi yapmaya yetkiniz yok.");
                if (transaction.Status != TransactionStatus.Accepted) return ServiceResult<Transaction>.FailureResult("Bu iþlem onaylanmamýþ veya zaten tamamlanmýþ.");
                if (transaction.Type != ProductType.Bagis) return ServiceResult<Transaction>.FailureResult("Bu iþlem bir baðýþ iþlemi deðil.");

                // Baðýþ iþlemi için 'PaymentStatus''u 'Paid' yapmaya gerek yok,
                // ancak 'CompleteTransactionInternalAsync' metodu 'IsSold' yapacaðý için
                // 'PaymentStatus'u 'Pending''den 'Paid''e çekmek, 
                // Converter'ýn butonu tekrar göstermemesi için mantýklýdýr.
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.UpdatedAt = DateTime.UtcNow;
                await transactionNode.PutAsync(transaction);

                // Satýþ modülü için yazdýðýmýz iç metodu TEKRAR KULLANIYORUZ.
                // Bu metot puanlarý ekler, ürünü 'IsSold' yapar ve bildirimi gönderir.
                return await CompleteTransactionInternalAsync(transaction);
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("Baðýþ onaylanýrken hata oluþtu.", ex.Message);
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


                // Hata ayýklama: Çekilen veri sayýsýný logla
                System.Diagnostics.Debug.WriteLine($"GetIncomingOffersAsync - Firebase'den {allTransactions.Count} kayýt çekildi.");


                var transactions = allTransactions.Select(t => {
                    var trans = t.Object;
                    trans.TransactionId = t.Key; // TransactionId'yi Firebase Key'den al
                    return trans;
                })
                     .OrderByDescending(t => t.CreatedAt)
                     .ToList();


                // Hata ayýklama: Filtrelenmiþ veri sayýsýný logla
                System.Diagnostics.Debug.WriteLine($"GetIncomingOffersAsync - Filtrelenmiþ {transactions.Count} teklif döndürülüyor.");


                return ServiceResult<List<Transaction>>.SuccessResult(transactions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HATA - GetIncomingOffersAsync: {ex.Message}");
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


                // Hata ayýklama: Çekilen veri sayýsýný logla
                System.Diagnostics.Debug.WriteLine($"GetMyOffersAsync - Firebase'den {allTransactions.Count} kayýt çekildi.");


                var transactions = allTransactions.Select(t => {
                    var trans = t.Object;
                    trans.TransactionId = t.Key; // TransactionId'yi Firebase Key'den al
                    return trans;
                })
                     .OrderByDescending(t => t.CreatedAt)
                     .ToList();


                // Hata ayýklama: Filtrelenmiþ veri sayýsýný logla
                System.Diagnostics.Debug.WriteLine($"GetMyOffersAsync - Filtrelenmiþ {transactions.Count} teklif döndürülüyor.");


                return ServiceResult<List<Transaction>>.SuccessResult(transactions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HATA - GetMyOffersAsync: {ex.Message}");
                return ServiceResult<List<Transaction>>.FailureResult("Gönderilen teklifler alýnamadý.", ex.Message);
            }
        }

    }
}