using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;
using System;
using System.Threading.Tasks;

namespace KamPay.Services
{
    public interface IQRCodeService
    {
        // GÜNCELLEME 1: Metot imzasý 4 parametre alacak þekilde düzeltildi.
        Task<ServiceResult<DeliveryQRCode>> GenerateDeliveryQRCodeAsync(string productId, string productTitle, string sellerId, string buyerId);
        Task<ServiceResult<DeliveryQRCode>> ValidateQRCodeAsync(string qrCodeData);
        Task<ServiceResult<bool>> CompleteDeliveryAsync(string qrCodeId);
        string GenerateQRCodeData(DeliveryQRCode delivery);
    }

    public class FirebaseQRCodeService : IQRCodeService
    {
        private readonly FirebaseClient _firebaseClient;
        private const string QRCodesCollection = "delivery_qrcodes";

        public FirebaseQRCodeService()
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
        }

        // GÜNCELLEME 2: Sýnýf içerisindeki metot imzasý da arayüzle ayný olacak þekilde düzeltildi.
        public async Task<ServiceResult<DeliveryQRCode>> GenerateDeliveryQRCodeAsync(string productId, string productTitle, string sellerId, string buyerId)
        {
            try
            {
                var delivery = new DeliveryQRCode
                {
                    ProductId = productId,
                    ProductTitle = productTitle, // Parametreden gelen deðer kullanýlýyor
                    SellerId = sellerId,
                    BuyerId = buyerId
                };

                delivery.QRCodeData = GenerateQRCodeData(delivery);

                await _firebaseClient
                    .Child(QRCodesCollection)
                    .Child(delivery.QRCodeId)
                    .PutAsync(delivery);

                return ServiceResult<DeliveryQRCode>.SuccessResult(delivery, "QR kod oluþturuldu");
            }
            catch (Exception ex)
            {
                return ServiceResult<DeliveryQRCode>.FailureResult("QR kod oluþturulamadý", ex.Message);
            }
        }

        public string GenerateQRCodeData(DeliveryQRCode delivery)
        {
            // Bu metodun içi ayný
            return $"KAMPAY|{delivery.QRCodeId}|{delivery.ProductId}|{delivery.CreatedAt.Ticks}";
        }

        public async Task<ServiceResult<DeliveryQRCode>> ValidateQRCodeAsync(string qrCodeData)
        {
            // Bu metodun içi ayný
            try
            {
                if (string.IsNullOrEmpty(qrCodeData) || !qrCodeData.StartsWith("KAMPAY|"))
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("Geçersiz QR kod");
                }
                var parts = qrCodeData.Split('|');
                if (parts.Length < 3)
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("QR kod formatý hatalý");
                }
                var qrCodeId = parts[1];
                var delivery = await _firebaseClient
                    .Child(QRCodesCollection)
                    .Child(qrCodeId)
                    .OnceSingleAsync<DeliveryQRCode>();
                if (delivery == null)
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("QR kod bulunamadý");
                }
                if (delivery.IsUsed)
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("QR kod daha önce kullanýlmýþ");
                }
                if (delivery.IsExpired)
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("QR kodun süresi dolmuþ");
                }
                return ServiceResult<DeliveryQRCode>.SuccessResult(delivery, "QR kod geçerli");
            }
            catch (Exception ex)
            {
                return ServiceResult<DeliveryQRCode>.FailureResult("Doðrulama hatasý", ex.Message);
            }
        }

        public async Task<ServiceResult<bool>> CompleteDeliveryAsync(string qrCodeId)
        {
            // Bu metodun içi ayný
            try
            {
                var delivery = await _firebaseClient
                    .Child(QRCodesCollection)
                    .Child(qrCodeId)
                    .OnceSingleAsync<DeliveryQRCode>();
                if (delivery == null)
                {
                    return ServiceResult<bool>.FailureResult("Teslimat bulunamadý");
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
                return ServiceResult<bool>.SuccessResult(true, "Teslimat tamamlandý!");
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.FailureResult("Teslimat tamamlanamadý", ex.Message);
            }
        }
    }
}