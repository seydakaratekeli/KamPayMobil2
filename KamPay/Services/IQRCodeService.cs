using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KamPay.Services
{
    public interface IQRCodeService
    {
        Task<ServiceResult<DeliveryQRCode>> GenerateDeliveryQRCodeAsync(string transactionId, string productId, string productTitle, string sellerId, string buyerId);
        Task<ServiceResult<DeliveryQRCode>> ValidateQRCodeAsync(string qrCodeData);
        Task<ServiceResult<bool>> CompleteDeliveryAsync(string qrCodeId);
        string GenerateQRCodeData(DeliveryQRCode delivery);
        Task<ServiceResult<List<DeliveryQRCode>>> GetQRCodesForTransactionAsync(string transactionId);
    }

    public class FirebaseQRCodeService : IQRCodeService
    {
        private readonly FirebaseClient _firebaseClient;
        // Puan servisini kullanmak i�in bir alan (field) ekliyoruz.
        private readonly IUserProfileService _userProfileService;
        private const string QRCodesCollection = "delivery_qrcodes";

        // HATA D�ZELTMES�: Constructor'� g�ncelleyerek IUserProfileService'i parametre olarak al�yoruz.
        public FirebaseQRCodeService(IUserProfileService userProfileService)
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _userProfileService = userProfileService; // Gelen servisi kendi alan�m�za at�yoruz.
        }

        public async Task<ServiceResult<bool>> CompleteDeliveryAsync(string qrCodeId)
        {
            try
            {
                var deliveryNode = _firebaseClient.Child(QRCodesCollection).Child(qrCodeId);
                var delivery = await deliveryNode.OnceSingleAsync<DeliveryQRCode>();

                if (delivery == null || delivery.IsUsed)
                {
                    return ServiceResult<bool>.FailureResult("Teslimat bulunamad� veya zaten tamamlanm��.");
                }

                delivery.IsUsed = true;
                delivery.UsedAt = DateTime.UtcNow;
                delivery.Status = DeliveryStatus.Completed;
                await deliveryNode.PutAsync(delivery);

                var transaction = await _firebaseClient.Child(Constants.TransactionsCollection).Child(delivery.TransactionId).OnceSingleAsync<Transaction>();
                if (transaction != null)
                {
                    var allCodesResult = await GetQRCodesForTransactionAsync(transaction.TransactionId);
                    if (allCodesResult.Success && allCodesResult.Data.All(c => c.IsUsed))
                    {
                        // T�M TESL�MATLAR B�TT�YSE:
                        await MarkProductAsSold(transaction.ProductId);
                        if (!string.IsNullOrEmpty(transaction.OfferedProductId))
                        {
                            await MarkProductAsSold(transaction.OfferedProductId);
                        }

                        transaction.Status = TransactionStatus.Completed;
                        await _firebaseClient.Child(Constants.TransactionsCollection).Child(transaction.TransactionId).PutAsync(transaction);

                        // PUANLARI EKLE
                        if (transaction.Type == ProductType.Bagis)
                        {
                            await _userProfileService.AddPointsForAction(transaction.BuyerId, UserAction.ReceiveDonation);
                        }
                        else
                        {
                            await _userProfileService.AddPointsForAction(transaction.SellerId, UserAction.CompleteTransaction);
                            await _userProfileService.AddPointsForAction(transaction.BuyerId, UserAction.CompleteTransaction);
                        }
                    }
                }

                return ServiceResult<bool>.SuccessResult(true, "Teslimat tamamland�!");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Teslimat tamamlanamad�", ex.Message);
            }
        }

        // ... Di�er metotlar�n�zda de�i�iklik yok ...
        #region Di�er Metotlar
        private async Task MarkProductAsSold(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return;
            var productNode = _firebaseClient.Child(Constants.ProductsCollection).Child(productId);
            var product = await productNode.OnceSingleAsync<Product>();
            if (product != null)
            {
                product.IsSold = true;
                product.SoldAt = DateTime.UtcNow;
               // product.IsActive = false;
                await productNode.PutAsync(product);
            }
        }
        public async Task<ServiceResult<DeliveryQRCode>> GenerateDeliveryQRCodeAsync(string transactionId, string productId, string productTitle, string sellerId, string buyerId)
        {
            try
            {
                var delivery = new DeliveryQRCode { TransactionId = transactionId, ProductId = productId, ProductTitle = productTitle, SellerId = sellerId, BuyerId = buyerId };
                delivery.QRCodeData = GenerateQRCodeData(delivery);
                await _firebaseClient.Child(QRCodesCollection).Child(delivery.QRCodeId).PutAsync(delivery);
                return ServiceResult<DeliveryQRCode>.SuccessResult(delivery, "QR kod olu�turuldu");
            }
            catch (Exception ex) { return ServiceResult<DeliveryQRCode>.FailureResult("QR kod olu�turulamad�", ex.Message); }
        }
        public async Task<ServiceResult<List<DeliveryQRCode>>> GetQRCodesForTransactionAsync(string transactionId)
        {
            try
            {
                var allCodes = await _firebaseClient.Child(QRCodesCollection).OnceAsync<DeliveryQRCode>();
                var qrCodes = allCodes.Select(q => q.Object).Where(q => q.TransactionId == transactionId).ToList();
                return ServiceResult<List<DeliveryQRCode>>.SuccessResult(qrCodes);
            }
            catch (Exception ex) { return ServiceResult<List<DeliveryQRCode>>.FailureResult("QR kodlar� al�namad�.", ex.Message); }
        }
        public string GenerateQRCodeData(DeliveryQRCode delivery) { return $"KAMPAY|{delivery.QRCodeId}|{delivery.ProductId}|{delivery.CreatedAt.Ticks}"; }
        public async Task<ServiceResult<DeliveryQRCode>> ValidateQRCodeAsync(string qrCodeData)
        {
            try
            {
                if (string.IsNullOrEmpty(qrCodeData) || !qrCodeData.StartsWith("KAMPAY|")) { return ServiceResult<DeliveryQRCode>.FailureResult("Ge�ersiz QR kod"); }
                var parts = qrCodeData.Split('|');
                if (parts.Length < 3) { return ServiceResult<DeliveryQRCode>.FailureResult("QR kod format� hatal�"); }
                var qrCodeId = parts[1];
                var delivery = await _firebaseClient.Child(QRCodesCollection).Child(qrCodeId).OnceSingleAsync<DeliveryQRCode>();
                if (delivery == null) { return ServiceResult<DeliveryQRCode>.FailureResult("QR kod bulunamad�"); }
                if (delivery.IsUsed) { return ServiceResult<DeliveryQRCode>.FailureResult("QR kod daha �nce kullan�lm��"); }
                if (delivery.IsExpired) { return ServiceResult<DeliveryQRCode>.FailureResult("QR kodun s�resi dolmu�"); }
                return ServiceResult<DeliveryQRCode>.SuccessResult(delivery, "QR kod ge�erli");
            }
            catch (Exception ex) { return ServiceResult<DeliveryQRCode>.FailureResult("Do�rulama hatas�", ex.Message); }
        }
        #endregion
    }
}