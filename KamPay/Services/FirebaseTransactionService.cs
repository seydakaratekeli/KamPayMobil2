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


        // Üst kýsýma ekle (FirebaseTransactionService sýnýfý içinde, constructor'dan önce)
        internal class TempOtpModel
        {
            public string Otp { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        private string GenerateOtp() => new Random().Next(100000, 999999).ToString();
        private string GenerateBankReference() => $"BTX-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 6)}";


        public FirebaseTransactionService(
          INotificationService notificationService,
          IProductService productService,
          IQRCodeService qrCodeService,
          IUserProfileService userProfileService) // UserProfileService eklendi
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _notificationService = notificationService;
            _productService = productService;
            _qrCodeService = qrCodeService;
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
                    // *** SATIÞ MODÜLÜ ÝÇÝN MESAJ GÜNCELLENDÝ ***
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
                }
                return ServiceResult<Transaction>.SuccessResult(transaction, "Teklif yanýtlandý.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("Ýþlem sýrasýnda hata oluþtu.", ex.Message);
            }
        }

        // --- BU METOTLAR HÝZMET MODÜLÜ ÝÇÝN KULLANILIR ---
        public async Task<ServiceResult<PaymentDto>> CreatePaymentSimulationAsync(string transactionId, string method)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();
                if (transaction == null) return ServiceResult<PaymentDto>.FailureResult("Ýþlem bulunamadý.");

                if (transaction.PaymentStatus != PaymentStatus.Pending)
                    return ServiceResult<PaymentDto>.FailureResult("Bu iþlem için ödeme zaten baþlatýlmýþ.");

                // Hizmet bedeli veya ürün bedeli (Hizmet için 'Price' kullanýlýyor olabilir)
                var amount = transaction.QuotedPrice > 0 ? transaction.QuotedPrice : (transaction.Price > 0 ? transaction.Price : 0m);

                var payment = new PaymentDto
                {
                    Amount = amount,
                    Currency = "TRY",
                    Status = ServicePaymentStatus.Initiated,
                    Method = method?.ToLower() switch
                    {
                        "cardsim" => PaymentMethodType.CardSim,
                        "banktransfersim" or "eft" or "havale" => PaymentMethodType.BankTransferSim,
                        _ => PaymentMethodType.CardSim
                    }
                };

                // Kart ödemesi ise OTP oluþtur ve Firebase'e kaydet
                if (payment.Method == PaymentMethodType.CardSim)
                {
                    var otp = GenerateOtp();
                    await _firebaseClient
                        .Child(Constants.TempOtpsCollection)
                        .Child(payment.PaymentId)
                        .PutAsync(new TempOtpModel
                        {
                            Otp = otp,
                            ExpiresAt = DateTime.UtcNow.AddMinutes(2)
                        });
                }

                // EFT ise banka referansý oluþtur
                if (payment.Method == PaymentMethodType.BankTransferSim)
                {
                    payment.BankName = "Ziraat Bankasý";
                    payment.BankReference = GenerateBankReference();
                }

                // Ýþlemi güncelle
                transaction.PaymentMethod = payment.Method;
                transaction.PaymentSimulationId = payment.PaymentId;
                transaction.PaymentStatus = PaymentStatus.Pending;
                await transactionNode.PutAsync(transaction);

                return ServiceResult<PaymentDto>.SuccessResult(payment, "Ödeme simülasyonu baþlatýldý.");
            }
            catch (Exception ex)
            {
                return ServiceResult<PaymentDto>.FailureResult("Simülasyon baþlatýlýrken hata.", ex.Message);
            }
        }

        // --- BU METOTLAR HÝZMET MODÜLÜ ÝÇÝN KULLANILIR ---
        public async Task<ServiceResult<bool>> ConfirmPaymentSimulationAsync(string transactionId, string paymentId, string? otp = null)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();
                if (transaction == null) return ServiceResult<bool>.FailureResult("Ýþlem bulunamadý.");

                if (transaction.PaymentSimulationId != paymentId)
                    return ServiceResult<bool>.FailureResult("Geçersiz ödeme kimliði.");

                if (transaction.PaymentMethod == PaymentMethodType.CardSim)
                {
                    var otpNode = _firebaseClient.Child(Constants.TempOtpsCollection).Child(paymentId);
                    var saved = await otpNode.OnceSingleAsync<TempOtpModel>();
                    if (saved == null) return ServiceResult<bool>.FailureResult("OTP bulunamadý.");
                    if (DateTime.UtcNow > saved.ExpiresAt)
                        return ServiceResult<bool>.FailureResult("OTP süresi doldu.");
                    if (string.IsNullOrWhiteSpace(otp) || saved.Otp != otp)
                        return ServiceResult<bool>.FailureResult("OTP geçersiz.");
                }

                // Baþarýlý ödeme
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.PaymentCompletedAt = DateTime.UtcNow;

                // DÝKKAT: Bu metot sadece ödemeyi onaylar.
                // Hizmet modülü, kendi akýþýnda 'CompleteRequest' adýmýnda
                // transaction.Status'ü 'Completed' yapmalýdýr.
                // Eðer bu metot SATIÞ için kullanýlsaydý, 'CompleteTransactionInternalAsync'i çaðýrmalýydý.
                // Ama HÝZMET için kullanýldýðýndan, sadece ödemeyi 'Paid' yapýyor.

                await transactionNode.PutAsync(transaction);

                // Bildirim gönder (Örn: Hizmet Saðlayýcýya)
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.SellerId,
                    Title = "Ödeme Alýndý!",
                    Message = $"{transaction.BuyerName}, '{transaction.ProductTitle}' hizmeti/ürünü için ödemesini tamamladý.",
                    Type = NotificationType.ProductSold, // Veya PaymentReceived
                    ActionUrl = nameof(Views.ServiceRequestsPage) // Veya OffersPage
                });

                return ServiceResult<bool>.SuccessResult(true, "Ödeme onaylandý.");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Ödeme onayýnda hata.", ex.Message);
            }
        }

        // --- BU METOT HÝZMET MODÜLÜ ÝÇÝNDÝR ---
        public async Task<ServiceResult<bool>> SimulatePaymentAndCompleteAsync(string transactionId)
        {
            // Bu metot, HÝZMET MODÜLÜ'nün kullandýðý karmaþýk simülasyon akýþýdýr.
            var payment = await CreatePaymentSimulationAsync(transactionId, "CardSim");
            if (!payment.Success) return ServiceResult<bool>.FailureResult(payment.Message);

            await Task.Delay(1500); // Simülasyon gecikmesi

            string otp = null;
            if (payment.Data.Method == PaymentMethodType.CardSim)
            {
                var otpNode = _firebaseClient.Child(Constants.TempOtpsCollection).Child(payment.Data.PaymentId);
                var savedOtp = await otpNode.OnceSingleAsync<TempOtpModel>();
                otp = savedOtp?.Otp;
            }

            var confirm = await ConfirmPaymentSimulationAsync(transactionId, payment.Data.PaymentId, otp: otp);

            // HÝZMET modülü akýþý burada bitiyor (ödeme tamamlandý). 
            // 'ServiceRequest'in 'Completed' yapýlmasý 'FirebaseServiceSharingService' içinde yönetiliyor.
            return confirm;
        }


        // --- YENÝ METOT: Sadece SATIÞ Modülü Ýçin Hýzlý Ödeme Tamamlama ---
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

                // *** EN ÖNEMLÝ KONTROL: Hizmet ile karýþmamasý için ***
                if (transaction.Type != ProductType.Satis) return ServiceResult<Transaction>.FailureResult("Bu iþlem bir satýþ iþlemi deðil.");


                // Simülasyon: Ödeme baþarýlý kabul ediliyor. (Hýzlý simülasyon)
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.PaymentMethod = PaymentMethodType.CardSim; // Hangi yöntemle olduðunu belirtelim
                transaction.PaymentCompletedAt = DateTime.UtcNow;
                transaction.UpdatedAt = DateTime.UtcNow;
                await transactionNode.PutAsync(transaction);

                // Ýþlemi tamamla (Internal metodu çaðýr: Ürünü satýldý yap, bildirim gönder, puan ekle)
                return await CompleteTransactionInternalAsync(transaction);
            }
            catch (Exception ex)
            {
                // Hata durumunda ödemeyi 'Failed' olarak iþaretle
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
                catch { /* Loglama */ }

                return ServiceResult<Transaction>.FailureResult("Ödeme tamamlanýrken hata oluþtu.", ex.Message);
            }
        }

        // --- YENÝ PRIVATE METOT: Ortak Tamamlama Ýþlemleri (Satýþ, Baðýþ, Takas için) ---
        private async Task<ServiceResult<Transaction>> CompleteTransactionInternalAsync(Transaction transaction)
        {
            try
            {
                // 1. Transaction durumunu 'Completed' yap
                transaction.Status = TransactionStatus.Completed;
                transaction.UpdatedAt = DateTime.UtcNow;
                await _firebaseClient.Child(Constants.TransactionsCollection).Child(transaction.TransactionId).PutAsync(transaction);

                // 2. Ürünü 'Satýldý' olarak iþaretle (IsActive=false yapar)
                await _productService.MarkAsSoldAsync(transaction.ProductId);

                // 3. Eðer Takas ise, teklif edilen ürünü de 'Satýldý' yap
                if (transaction.Type == ProductType.Takas && !string.IsNullOrEmpty(transaction.OfferedProductId))
                {
                    await _productService.MarkAsSoldAsync(transaction.OfferedProductId);
                }

                // 4. Bildirimleri gönder
                // Satýcýya:
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.SellerId,
                    Type = NotificationType.ProductSold,
                    Title = transaction.Type == ProductType.Bagis ? "Baðýþ Tamamlandý!" : (transaction.Type == ProductType.Takas ? "Takas Tamamlandý!" : "Ürünün Satýldý!"),
                    Message = $"'{transaction.ProductTitle}' için '{transaction.BuyerName}' ile olan iþleminiz tamamlandý.",
                    ActionUrl = nameof(Views.OffersPage)
                });
                // Alýcýya:
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.BuyerId,
                    Type = NotificationType.ProductSold,
                    Title = transaction.Type == ProductType.Bagis ? "Baðýþ Teslim Alýndý!" : (transaction.Type == ProductType.Takas ? "Takas Tamamlandý!" : "Satýn Alma Tamamlandý!"),
                    Message = $"'{transaction.ProductTitle}' ürünü için '{transaction.SellerName}' ile olan iþleminiz tamamlandý.",
                    ActionUrl = nameof(Views.OffersPage)
                });

                // 5. Puanlarý ekle (Tipe göre ayrým)
                if (transaction.Type == ProductType.Bagis)
                {
                    await _userProfileService.AddPointsForAction(transaction.SellerId, UserAction.MakeDonation);
                    await _userProfileService.AddPointsForAction(transaction.BuyerId, UserAction.ReceiveDonation);
                }
                else // Satýþ veya Takas
                {
                    await _userProfileService.AddPointsForAction(transaction.SellerId, UserAction.CompleteTransaction);
                    await _userProfileService.AddPointsForAction(transaction.BuyerId, UserAction.CompleteTransaction);
                }

                return ServiceResult<Transaction>.SuccessResult(transaction, "Ýþlem baþarýyla tamamlandý.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Hata - CompleteTransactionInternalAsync: {ex.Message}");
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
                    Type = product.Type, // Product'tan gelen tipi kullanýyoruz
                    SellerId = product.UserId,
                    SellerName = product.UserName,
                    BuyerId = buyer.UserId,
                    BuyerName = buyer.FullName,
                    Status = TransactionStatus.Pending,
                    PaymentStatus = PaymentStatus.Pending, // Baþlangýçta ödeme bekliyor
                    Price = product.Price // Ürünün fiyatýný da ekleyelim
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
                    Title = product.Type == ProductType.Bagis ? "Yeni Baðýþ Talebi!" : (product.Type == ProductType.Takas ? "Yeni Takas Teklifi!" : "Yeni Satýþ Teklifi!"),
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
                var offeredProductResult = await _productService.GetProductByIdAsync(offeredProductId);
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
                    PaymentStatus = PaymentStatus.Pending // Takasta ödeme 'N/A' (Uygulanamaz) olabilir, ama 'Pending' kalmasý da sorun yaratmaz.
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

        // --- YENÝ METOT: BAÐIÞ Onaylama ---
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

                // Baðýþta ödeme olmadýðý için PaymentStatus'ü 'Paid' yapmak,
                // Converter'ýn (SimulatePaymentButtonVisibilityConverter) butonu tekrar göstermemesi için önemlidir.
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.UpdatedAt = DateTime.UtcNow;
                await transactionNode.PutAsync(transaction);

                // Satýþ modülü için yazdýðýmýz iç metodu TEKRAR KULLANIYORUZ.
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

                var transactions = allTransactions.Select(t => {
                    var trans = t.Object;
                    trans.TransactionId = t.Key;
                    return trans;
                })
                    .OrderByDescending(t => t.CreatedAt)
                    .ToList();

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

                var transactions = allTransactions.Select(t => {
                    var trans = t.Object;
                    trans.TransactionId = t.Key;
                    return trans;
                })
                    .OrderByDescending(t => t.CreatedAt)
                    .ToList();

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