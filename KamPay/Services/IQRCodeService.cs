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
        // G�NCELLEME: Metot imzas�na 'transactionId' eklendi.
        Task<ServiceResult<DeliveryQRCode>> GenerateDeliveryQRCodeAsync(string transactionId, string productId, string productTitle, string sellerId, string buyerId);
        Task<ServiceResult<DeliveryQRCode>> ValidateQRCodeAsync(string qrCodeData);
        Task<ServiceResult<bool>> CompleteDeliveryAsync(string qrCodeId);
        string GenerateQRCodeData(DeliveryQRCode delivery);
        Task<ServiceResult<List<DeliveryQRCode>>> GetQRCodesForTransactionAsync(string transactionId);
    }

    public class FirebaseQRCodeService : IQRCodeService
    {
        private readonly FirebaseClient _firebaseClient;
        private const string QRCodesCollection = "delivery_qrcodes";

        public FirebaseQRCodeService()
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
        }

        // G�NCELLEME: 'transactionId' parametresi art�k al�n�yor ve modele atan�yor.
        public async Task<ServiceResult<DeliveryQRCode>> GenerateDeliveryQRCodeAsync(string transactionId, string productId, string productTitle, string sellerId, string buyerId)
        {
            try
            {
                var delivery = new DeliveryQRCode
                {
                    TransactionId = transactionId, // Yeni eklenen sat�r
                    ProductId = productId,
                    ProductTitle = productTitle,
                    SellerId = sellerId,
                    BuyerId = buyerId
                };

                delivery.QRCodeData = GenerateQRCodeData(delivery);

                await _firebaseClient
                    .Child(QRCodesCollection)
                    .Child(delivery.QRCodeId)
                    .PutAsync(delivery);

                return ServiceResult<DeliveryQRCode>.SuccessResult(delivery, "QR kod olu�turuldu");
            }
            catch (Exception ex)
            {
                return ServiceResult<DeliveryQRCode>.FailureResult("QR kod olu�turulamad�", ex.Message);
            }
        }

        // Bu metot art�k do�ru �al��acak ��nk� modelde 'TransactionId' var.
        public async Task<ServiceResult<List<DeliveryQRCode>>> GetQRCodesForTransactionAsync(string transactionId)
        {
            try
            {
                var allCodes = await _firebaseClient
                    .Child(QRCodesCollection)
                    .OnceAsync<DeliveryQRCode>();

                var qrCodes = allCodes
                    .Select(q => q.Object)
                    .Where(q => q.TransactionId == transactionId)
                    .ToList();

                return ServiceResult<List<DeliveryQRCode>>.SuccessResult(qrCodes);
            }
            catch (Exception ex)
            {
                return ServiceResult<List<DeliveryQRCode>>.FailureResult("QR kodlar� al�namad�.", ex.Message);
            }
        }

        // ... Di�er metotlar (CompleteDeliveryAsync, ValidateQRCodeAsync, GenerateQRCodeData) ayn� kalabilir ...
       
        public string GenerateQRCodeData(DeliveryQRCode delivery)
        {
            return $"KAMPAY|{delivery.QRCodeId}|{delivery.ProductId}|{delivery.CreatedAt.Ticks}";
        }

        public async Task<ServiceResult<DeliveryQRCode>> ValidateQRCodeAsync(string qrCodeData)
        {
            try
            {
                if (string.IsNullOrEmpty(qrCodeData) || !qrCodeData.StartsWith("KAMPAY|"))
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("Ge�ersiz QR kod");
                }
                var parts = qrCodeData.Split('|');
                if (parts.Length < 3)
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("QR kod format� hatal�");
                }
                var qrCodeId = parts[1];
                var delivery = await _firebaseClient
                    .Child(QRCodesCollection)
                    .Child(qrCodeId)
                    .OnceSingleAsync<DeliveryQRCode>();
                if (delivery == null)
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("QR kod bulunamad�");
                }
                if (delivery.IsUsed)
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("QR kod daha �nce kullan�lm��");
                }
                if (delivery.IsExpired)
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("QR kodun s�resi dolmu�");
                }
                return ServiceResult<DeliveryQRCode>.SuccessResult(delivery, "QR kod ge�erli");
            }
            catch (Exception ex)
            {
                return ServiceResult<DeliveryQRCode>.FailureResult("Do�rulama hatas�", ex.Message);
            }
        }

        public async Task<ServiceResult<bool>> CompleteDeliveryAsync(string qrCodeId)
        {
            try
            {
                var delivery = await _firebaseClient
                    .Child(QRCodesCollection)
                    .Child(qrCodeId)
                    .OnceSingleAsync<DeliveryQRCode>();
                if (delivery == null)
                {
                    return ServiceResult<bool>.FailureResult("Teslimat bulunamad�");
                }
                delivery.IsUsed = true;
                delivery.UsedAt = DateTime.UtcNow;
                delivery.Status = DeliveryStatus.Completed;
                await _firebaseClient
                    .Child(QRCodesCollection)
                    .Child(qrCodeId)
                    .PutAsync(delivery);
                var product = await _firebaseClient
                    .Child(Constants.ProductsCollection)
                    .Child(delivery.ProductId)
                    .OnceSingleAsync<Product>();
                if (product != null)
                {
                    product.IsSold = true;
                    product.SoldAt = DateTime.UtcNow;
                    await _firebaseClient
                        .Child(Constants.ProductsCollection)
                        .Child(delivery.ProductId)
                        .PutAsync(product);
                }
                return ServiceResult<bool>.SuccessResult(true, "Teslimat tamamland�!");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Teslimat tamamlanamad�", ex.Message);
            }
        }
    }
}