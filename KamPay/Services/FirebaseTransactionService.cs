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
        private readonly IUserProfileService _userProfileService; // Puan i�in eklendi

        public FirebaseTransactionService(
          INotificationService notificationService,
          IProductService productService,
          IQRCodeService qrCodeService,
          IUserProfileService userProfileService) // UserProfileService eklendi // <-- Buraya eklendi
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
            _productService = productService;
            _qrCodeService = qrCodeService; // <-- Gelen servis de�i�kene atan�yor
            _userProfileService = userProfileService; // Atama yap�ld�
        }


        public async Task<ServiceResult<Transaction>> RespondToOfferAsync(string transactionId, bool accept)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();

                if (transaction == null) return ServiceResult<Transaction>.FailureResult("��lem bulunamad�.");
                if (transaction.Status != TransactionStatus.Pending) return ServiceResult<Transaction>.SuccessResult(transaction, "Bu teklif zaten yan�tlanm��.");

                transaction.Status = accept ? TransactionStatus.Accepted : TransactionStatus.Rejected;
                transaction.UpdatedAt = DateTime.UtcNow;

                await transactionNode.PutAsync(transaction);

                // Al�c�ya bildirim g�nder (mevcut kod)
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.BuyerId,
                    Type = accept ? NotificationType.OfferAccepted : NotificationType.OfferRejected,
                    Title = accept ? "Teklifin Kabul Edildi!" : "Teklifin Reddedildi",
                    Message = $"'{transaction.SellerName}', '{transaction.ProductTitle}' �r�n� i�in yapt���n teklifi {(accept ? "kabul etti. �imdi �demeyi tamamlayabilirsin." : "reddetti.")}",
                    ActionUrl = nameof(Views.OffersPage) // Giden Tekliflerim'e y�nlendirsin
                });

                if (accept)
                {
                    // �r�nleri rezerve et
                    await _productService.MarkAsReservedAsync(transaction.ProductId, true);

                    if (transaction.Type == ProductType.Takas && !string.IsNullOrEmpty(transaction.OfferedProductId))
                    {
                        await _productService.MarkAsReservedAsync(transaction.OfferedProductId, true);
                    }

                    // --- QR KOD OLU�TURMA KISMI (�STE��N�Z �ZER�NE DEVRE DI�I) ---
                    /*
                    await _qrCodeService.GenerateDeliveryQRCodeAsync(transaction.TransactionId, transaction.ProductId, transaction.ProductTitle, transaction.SellerId, transaction.BuyerId);
                    if (transaction.Type == ProductType.Takas && !string.IsNullOrEmpty(transaction.OfferedProductId))
                    {
                        await _qrCodeService.GenerateDeliveryQRCodeAsync(transaction.TransactionId, transaction.OfferedProductId, transaction.OfferedProductTitle, transaction.BuyerId, transaction.SellerId);
                    }
                    */
                    // --- --- ---
                }
                return ServiceResult<Transaction>.SuccessResult(transaction, "Teklif yan�tland�.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("��lem s�ras�nda hata olu�tu.", ex.Message);
            }
        }
        // --- YEN� METOT: CompletePaymentAsync ---
        public async Task<ServiceResult<Transaction>> CompletePaymentAsync(string transactionId, string buyerId)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();

                // Kontroller
                if (transaction == null) return ServiceResult<Transaction>.FailureResult("��lem bulunamad�.");
                if (transaction.BuyerId != buyerId) return ServiceResult<Transaction>.FailureResult("Bu i�lemi yapmaya yetkiniz yok.");
                if (transaction.Status != TransactionStatus.Accepted) return ServiceResult<Transaction>.FailureResult("Bu i�lem onaylanmam�� veya zaten tamamlanm��.");
                if (transaction.PaymentStatus != PaymentStatus.Pending) return ServiceResult<Transaction>.FailureResult("Bu i�lemin �demesi zaten yap�lm�� veya ba�ar�s�z olmu�.");
                if (transaction.Type != ProductType.Satis) return ServiceResult<Transaction>.FailureResult("Bu i�lem bir sat�� i�lemi de�il.");


                // Sim�lasyon: �deme ba�ar�l� kabul ediliyor.
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.UpdatedAt = DateTime.UtcNow;
                await transactionNode.PutAsync(transaction);

                // ��lemi tamamla (Internal metodu �a��r)
                return await CompleteTransactionInternalAsync(transaction);

            }
            catch (Exception ex)
            {
                // �deme ba�ar�s�z olursa durumu g�ncelle (Opsiyonel ama iyi pratik)
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
                catch { /* Loglama yap�labilir */ }

                return ServiceResult<Transaction>.FailureResult("�deme tamamlan�rken hata olu�tu.", ex.Message);
            }
        }

        // --- YEN� PRIVATE METOT: CompleteTransactionInternalAsync ---
        private async Task<ServiceResult<Transaction>> CompleteTransactionInternalAsync(Transaction transaction)
        {
            try
            {
                // 1. Transaction durumunu 'Completed' yap
                transaction.Status = TransactionStatus.Completed;
                transaction.UpdatedAt = DateTime.UtcNow;
                await _firebaseClient.Child(Constants.TransactionsCollection).Child(transaction.TransactionId).PutAsync(transaction);

                // 2. �r�n� 'Sat�ld�' olarak i�aretle (Art�k rezerve de�il, tamamen sat�ld�)
                // Dikkat: MarkAsSoldAsync �r�n� IsActive=false yapar ve listeden kald�r�r.
                await _productService.MarkAsSoldAsync(transaction.ProductId);

                // 3. Bildirimleri g�nder
                // Sat�c�ya:
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.SellerId,
                    Type = NotificationType.ProductSold,
                    Title = "�r�n�n Sat�ld�!",
                    Message = $"'{transaction.ProductTitle}' �r�n�n '{transaction.BuyerName}' taraf�ndan sat�n al�nd� ve i�lem tamamland�.",
                    ActionUrl = nameof(Views.OffersPage) // Gelen Teklifler'e gidebilir
                });
                // Al�c�ya:
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.BuyerId,
                    Type = NotificationType.ProductSold, // Ayn� tip kullan�labilir veya yeni tip eklenebilir
                    Title = "Sat�n Alma Tamamland�!",
                    Message = $"'{transaction.ProductTitle}' �r�n�n� ba�ar�yla sat�n ald�n.",
                    ActionUrl = nameof(Views.OffersPage) // Giden Teklifler'e gidebilir
                });

                // 4. Puanlar� ekle
                await _userProfileService.AddPointsForAction(transaction.SellerId, UserAction.CompleteTransaction);
                await _userProfileService.AddPointsForAction(transaction.BuyerId, UserAction.CompleteTransaction); // Al�c�ya da puan eklenebilir.

                return ServiceResult<Transaction>.SuccessResult(transaction, "��lem ba�ar�yla tamamland�.");

            }
            catch (Exception ex)
            {
                // Hata durumunda i�lemi geri almak zor olabilir, bu y�zden loglama �nemli.
                Console.WriteLine($"Hata - CompleteTransactionInternalAsync: {ex.Message}");
                // Belki transaction durumunu 'Failed' gibi bir �eye �ekebiliriz?
                return ServiceResult<Transaction>.FailureResult("��lem tamamlan�rken bir hata olu�tu.", ex.Message);
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
                    Type = product.Type, // D�KKAT: Product'tan gelen tipi kullan�yoruz
                    SellerId = product.UserId,
                    SellerName = product.UserName, // Product modelinde UserName oldu�unu varsay�yorum
                    BuyerId = buyer.UserId,
                    BuyerName = buyer.FullName,
                    Status = TransactionStatus.Pending,
                    PaymentStatus = PaymentStatus.Pending // Ba�lang��ta �deme bekliyor
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
                    Title = product.Type == ProductType.Bagis ? "Yeni Ba��� Talebi!" : "Yeni Sat�� Teklifi!",
                    Message = $"{buyer.FullName}, '{product.Title}' �r�n�n i�in bir {(product.Type == ProductType.Bagis ? "talep" : "teklif")} g�nderdi.",
                    ActionUrl = nameof(Views.OffersPage) // Gelen Teklifler sayfas�
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
                var offeredProductResult = await _productService.GetProductByIdAsync(offeredProductId); // ProductService kullanmak daha iyi
                if (!offeredProductResult.Success || offeredProductResult.Data == null)
                {
                    return ServiceResult<Transaction>.FailureResult("Teklif edilen �r�n bulunamad�.");
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
                    PaymentStatus = PaymentStatus.Pending // Takasta �deme durumu her zaman Pending kalabilir veya kald�r�labilir.
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

        // FirebaseTransactionService.cs i�ine yeni metot
        public async Task<ServiceResult<Transaction>> ConfirmDonationAsync(string transactionId, string buyerId)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();

                // Kontroller
                if (transaction == null) return ServiceResult<Transaction>.FailureResult("��lem bulunamad�.");
                if (transaction.BuyerId != buyerId) return ServiceResult<Transaction>.FailureResult("Bu i�lemi yapmaya yetkiniz yok.");
                if (transaction.Status != TransactionStatus.Accepted) return ServiceResult<Transaction>.FailureResult("Bu i�lem onaylanmam�� veya zaten tamamlanm��.");
                if (transaction.Type != ProductType.Bagis) return ServiceResult<Transaction>.FailureResult("Bu i�lem bir ba��� i�lemi de�il.");

                // Ba��� i�lemi i�in 'PaymentStatus''u 'Paid' yapmaya gerek yok,
                // ancak 'CompleteTransactionInternalAsync' metodu 'IsSold' yapaca�� i�in
                // 'PaymentStatus'u 'Pending''den 'Paid''e �ekmek, 
                // Converter'�n butonu tekrar g�stermemesi i�in mant�kl�d�r.
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.UpdatedAt = DateTime.UtcNow;
                await transactionNode.PutAsync(transaction);

                // Sat�� mod�l� i�in yazd���m�z i� metodu TEKRAR KULLANIYORUZ.
                // Bu metot puanlar� ekler, �r�n� 'IsSold' yapar ve bildirimi g�nderir.
                return await CompleteTransactionInternalAsync(transaction);
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("Ba��� onaylan�rken hata olu�tu.", ex.Message);
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


                // Hata ay�klama: �ekilen veri say�s�n� logla
                System.Diagnostics.Debug.WriteLine($"GetIncomingOffersAsync - Firebase'den {allTransactions.Count} kay�t �ekildi.");


                var transactions = allTransactions.Select(t => {
                    var trans = t.Object;
                    trans.TransactionId = t.Key; // TransactionId'yi Firebase Key'den al
                    return trans;
                })
                     .OrderByDescending(t => t.CreatedAt)
                     .ToList();


                // Hata ay�klama: Filtrelenmi� veri say�s�n� logla
                System.Diagnostics.Debug.WriteLine($"GetIncomingOffersAsync - Filtrelenmi� {transactions.Count} teklif d�nd�r�l�yor.");


                return ServiceResult<List<Transaction>>.SuccessResult(transactions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HATA - GetIncomingOffersAsync: {ex.Message}");
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


                // Hata ay�klama: �ekilen veri say�s�n� logla
                System.Diagnostics.Debug.WriteLine($"GetMyOffersAsync - Firebase'den {allTransactions.Count} kay�t �ekildi.");


                var transactions = allTransactions.Select(t => {
                    var trans = t.Object;
                    trans.TransactionId = t.Key; // TransactionId'yi Firebase Key'den al
                    return trans;
                })
                     .OrderByDescending(t => t.CreatedAt)
                     .ToList();


                // Hata ay�klama: Filtrelenmi� veri say�s�n� logla
                System.Diagnostics.Debug.WriteLine($"GetMyOffersAsync - Filtrelenmi� {transactions.Count} teklif d�nd�r�l�yor.");


                return ServiceResult<List<Transaction>>.SuccessResult(transactions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HATA - GetMyOffersAsync: {ex.Message}");
                return ServiceResult<List<Transaction>>.FailureResult("G�nderilen teklifler al�namad�.", ex.Message);
            }
        }

    }
}