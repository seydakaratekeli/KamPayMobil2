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


        // �st k�s�ma ekle (FirebaseTransactionService s�n�f� i�inde, constructor'dan �nce)
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
                    // *** SATI� MOD�L� ���N MESAJ G�NCELLEND� ***
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
                }
                return ServiceResult<Transaction>.SuccessResult(transaction, "Teklif yan�tland�.");
            }
            catch (Exception ex)
            {
                return ServiceResult<Transaction>.FailureResult("��lem s�ras�nda hata olu�tu.", ex.Message);
            }
        }

        // --- BU METOTLAR H�ZMET MOD�L� ���N KULLANILIR ---
        public async Task<ServiceResult<PaymentDto>> CreatePaymentSimulationAsync(string transactionId, string method)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();
                if (transaction == null) return ServiceResult<PaymentDto>.FailureResult("��lem bulunamad�.");

                if (transaction.PaymentStatus != PaymentStatus.Pending)
                    return ServiceResult<PaymentDto>.FailureResult("Bu i�lem i�in �deme zaten ba�lat�lm��.");

                // Hizmet bedeli veya �r�n bedeli (Hizmet i�in 'Price' kullan�l�yor olabilir)
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

                // Kart �demesi ise OTP olu�tur ve Firebase'e kaydet
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

                // EFT ise banka referans� olu�tur
                if (payment.Method == PaymentMethodType.BankTransferSim)
                {
                    payment.BankName = "Ziraat Bankas�";
                    payment.BankReference = GenerateBankReference();
                }

                // ��lemi g�ncelle
                transaction.PaymentMethod = payment.Method;
                transaction.PaymentSimulationId = payment.PaymentId;
                transaction.PaymentStatus = PaymentStatus.Pending;
                await transactionNode.PutAsync(transaction);

                return ServiceResult<PaymentDto>.SuccessResult(payment, "�deme sim�lasyonu ba�lat�ld�.");
            }
            catch (Exception ex)
            {
                return ServiceResult<PaymentDto>.FailureResult("Sim�lasyon ba�lat�l�rken hata.", ex.Message);
            }
        }

        // --- BU METOTLAR H�ZMET MOD�L� ���N KULLANILIR ---
        public async Task<ServiceResult<bool>> ConfirmPaymentSimulationAsync(string transactionId, string paymentId, string? otp = null)
        {
            try
            {
                var transactionNode = _firebaseClient.Child(Constants.TransactionsCollection).Child(transactionId);
                var transaction = await transactionNode.OnceSingleAsync<Transaction>();
                if (transaction == null) return ServiceResult<bool>.FailureResult("��lem bulunamad�.");

                if (transaction.PaymentSimulationId != paymentId)
                    return ServiceResult<bool>.FailureResult("Ge�ersiz �deme kimli�i.");

                if (transaction.PaymentMethod == PaymentMethodType.CardSim)
                {
                    var otpNode = _firebaseClient.Child(Constants.TempOtpsCollection).Child(paymentId);
                    var saved = await otpNode.OnceSingleAsync<TempOtpModel>();
                    if (saved == null) return ServiceResult<bool>.FailureResult("OTP bulunamad�.");
                    if (DateTime.UtcNow > saved.ExpiresAt)
                        return ServiceResult<bool>.FailureResult("OTP s�resi doldu.");
                    if (string.IsNullOrWhiteSpace(otp) || saved.Otp != otp)
                        return ServiceResult<bool>.FailureResult("OTP ge�ersiz.");
                }

                // Ba�ar�l� �deme
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.PaymentCompletedAt = DateTime.UtcNow;

                // D�KKAT: Bu metot sadece �demeyi onaylar.
                // Hizmet mod�l�, kendi ak���nda 'CompleteRequest' ad�m�nda
                // transaction.Status'� 'Completed' yapmal�d�r.
                // E�er bu metot SATI� i�in kullan�lsayd�, 'CompleteTransactionInternalAsync'i �a��rmal�yd�.
                // Ama H�ZMET i�in kullan�ld���ndan, sadece �demeyi 'Paid' yap�yor.

                await transactionNode.PutAsync(transaction);

                // Bildirim g�nder (�rn: Hizmet Sa�lay�c�ya)
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.SellerId,
                    Title = "�deme Al�nd�!",
                    Message = $"{transaction.BuyerName}, '{transaction.ProductTitle}' hizmeti/�r�n� i�in �demesini tamamlad�.",
                    Type = NotificationType.ProductSold, // Veya PaymentReceived
                    ActionUrl = nameof(Views.ServiceRequestsPage) // Veya OffersPage
                });

                return ServiceResult<bool>.SuccessResult(true, "�deme onayland�.");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("�deme onay�nda hata.", ex.Message);
            }
        }

        // --- BU METOT H�ZMET MOD�L� ���ND�R ---
        public async Task<ServiceResult<bool>> SimulatePaymentAndCompleteAsync(string transactionId)
        {
            // Bu metot, H�ZMET MOD�L�'n�n kulland��� karma��k sim�lasyon ak���d�r.
            var payment = await CreatePaymentSimulationAsync(transactionId, "CardSim");
            if (!payment.Success) return ServiceResult<bool>.FailureResult(payment.Message);

            await Task.Delay(1500); // Sim�lasyon gecikmesi

            string otp = null;
            if (payment.Data.Method == PaymentMethodType.CardSim)
            {
                var otpNode = _firebaseClient.Child(Constants.TempOtpsCollection).Child(payment.Data.PaymentId);
                var savedOtp = await otpNode.OnceSingleAsync<TempOtpModel>();
                otp = savedOtp?.Otp;
            }

            var confirm = await ConfirmPaymentSimulationAsync(transactionId, payment.Data.PaymentId, otp: otp);

            // H�ZMET mod�l� ak��� burada bitiyor (�deme tamamland�). 
            // 'ServiceRequest'in 'Completed' yap�lmas� 'FirebaseServiceSharingService' i�inde y�netiliyor.
            return confirm;
        }


        // --- YEN� METOT: Sadece SATI� Mod�l� ��in H�zl� �deme Tamamlama ---
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

                // *** EN �NEML� KONTROL: Hizmet ile kar��mamas� i�in ***
                if (transaction.Type != ProductType.Satis) return ServiceResult<Transaction>.FailureResult("Bu i�lem bir sat�� i�lemi de�il.");


                // Sim�lasyon: �deme ba�ar�l� kabul ediliyor. (H�zl� sim�lasyon)
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.PaymentMethod = PaymentMethodType.CardSim; // Hangi y�ntemle oldu�unu belirtelim
                transaction.PaymentCompletedAt = DateTime.UtcNow;
                transaction.UpdatedAt = DateTime.UtcNow;
                await transactionNode.PutAsync(transaction);

                // ��lemi tamamla (Internal metodu �a��r: �r�n� sat�ld� yap, bildirim g�nder, puan ekle)
                return await CompleteTransactionInternalAsync(transaction);
            }
            catch (Exception ex)
            {
                // Hata durumunda �demeyi 'Failed' olarak i�aretle
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

                return ServiceResult<Transaction>.FailureResult("�deme tamamlan�rken hata olu�tu.", ex.Message);
            }
        }

        // --- YEN� PRIVATE METOT: Ortak Tamamlama ��lemleri (Sat��, Ba���, Takas i�in) ---
        private async Task<ServiceResult<Transaction>> CompleteTransactionInternalAsync(Transaction transaction)
        {
            try
            {
                // 1. Transaction durumunu 'Completed' yap
                transaction.Status = TransactionStatus.Completed;
                transaction.UpdatedAt = DateTime.UtcNow;
                await _firebaseClient.Child(Constants.TransactionsCollection).Child(transaction.TransactionId).PutAsync(transaction);

                // 2. �r�n� 'Sat�ld�' olarak i�aretle (IsActive=false yapar)
                await _productService.MarkAsSoldAsync(transaction.ProductId);

                // 3. E�er Takas ise, teklif edilen �r�n� de 'Sat�ld�' yap
                if (transaction.Type == ProductType.Takas && !string.IsNullOrEmpty(transaction.OfferedProductId))
                {
                    await _productService.MarkAsSoldAsync(transaction.OfferedProductId);
                }

                // 4. Bildirimleri g�nder
                // Sat�c�ya:
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.SellerId,
                    Type = NotificationType.ProductSold,
                    Title = transaction.Type == ProductType.Bagis ? "Ba��� Tamamland�!" : (transaction.Type == ProductType.Takas ? "Takas Tamamland�!" : "�r�n�n Sat�ld�!"),
                    Message = $"'{transaction.ProductTitle}' i�in '{transaction.BuyerName}' ile olan i�leminiz tamamland�.",
                    ActionUrl = nameof(Views.OffersPage)
                });
                // Al�c�ya:
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = transaction.BuyerId,
                    Type = NotificationType.ProductSold,
                    Title = transaction.Type == ProductType.Bagis ? "Ba��� Teslim Al�nd�!" : (transaction.Type == ProductType.Takas ? "Takas Tamamland�!" : "Sat�n Alma Tamamland�!"),
                    Message = $"'{transaction.ProductTitle}' �r�n� i�in '{transaction.SellerName}' ile olan i�leminiz tamamland�.",
                    ActionUrl = nameof(Views.OffersPage)
                });

                // 5. Puanlar� ekle (Tipe g�re ayr�m)
                if (transaction.Type == ProductType.Bagis)
                {
                    await _userProfileService.AddPointsForAction(transaction.SellerId, UserAction.MakeDonation);
                    await _userProfileService.AddPointsForAction(transaction.BuyerId, UserAction.ReceiveDonation);
                }
                else // Sat�� veya Takas
                {
                    await _userProfileService.AddPointsForAction(transaction.SellerId, UserAction.CompleteTransaction);
                    await _userProfileService.AddPointsForAction(transaction.BuyerId, UserAction.CompleteTransaction);
                }

                return ServiceResult<Transaction>.SuccessResult(transaction, "��lem ba�ar�yla tamamland�.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Hata - CompleteTransactionInternalAsync: {ex.Message}");
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
                    Type = product.Type, // Product'tan gelen tipi kullan�yoruz
                    SellerId = product.UserId,
                    SellerName = product.UserName,
                    BuyerId = buyer.UserId,
                    BuyerName = buyer.FullName,
                    Status = TransactionStatus.Pending,
                    PaymentStatus = PaymentStatus.Pending, // Ba�lang��ta �deme bekliyor
                    Price = product.Price // �r�n�n fiyat�n� da ekleyelim
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
                    Title = product.Type == ProductType.Bagis ? "Yeni Ba��� Talebi!" : (product.Type == ProductType.Takas ? "Yeni Takas Teklifi!" : "Yeni Sat�� Teklifi!"),
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
                var offeredProductResult = await _productService.GetProductByIdAsync(offeredProductId);
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
                    PaymentStatus = PaymentStatus.Pending // Takasta �deme 'N/A' (Uygulanamaz) olabilir, ama 'Pending' kalmas� da sorun yaratmaz.
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

        // --- YEN� METOT: BA�I� Onaylama ---
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

                // Ba���ta �deme olmad��� i�in PaymentStatus'� 'Paid' yapmak,
                // Converter'�n (SimulatePaymentButtonVisibilityConverter) butonu tekrar g�stermemesi i�in �nemlidir.
                transaction.PaymentStatus = PaymentStatus.Paid;
                transaction.UpdatedAt = DateTime.UtcNow;
                await transactionNode.PutAsync(transaction);

                // Sat�� mod�l� i�in yazd���m�z i� metodu TEKRAR KULLANIYORUZ.
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
                return ServiceResult<List<Transaction>>.FailureResult("G�nderilen teklifler al�namad�.", ex.Message);
            }
        }
    }
}