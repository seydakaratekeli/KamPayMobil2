using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;

namespace KamPay.Services
{
    public interface IQRCodeService
    {
        Task<ServiceResult<DeliveryQRCode>> GenerateDeliveryQRCodeAsync(string productId, string sellerId, string buyerId);
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

        public async Task<ServiceResult<DeliveryQRCode>> GenerateDeliveryQRCodeAsync(string productId, string sellerId, string buyerId)
        {
            try
            {
                var delivery = new DeliveryQRCode
                {
                    ProductId = productId,
                    SellerId = sellerId,
                    BuyerId = buyerId
                };

                // QR kod datasýný oluþtur
                delivery.QRCodeData = GenerateQRCodeData(delivery);

                // Firebase'e kaydet
                await _firebaseClient
                    .Child(QRCodesCollection)
                    .Child(delivery.QRCodeId)
                    .PutAsync(delivery);

                return ServiceResult<DeliveryQRCode>.SuccessResult(
                    delivery,
                    "QR kod oluþturuldu"
                );
            }
            catch (Exception ex)
            {
                return ServiceResult<DeliveryQRCode>.FailureResult(
                    "QR kod oluþturulamadý",
                    ex.Message
                );
            }
        }

        public string GenerateQRCodeData(DeliveryQRCode delivery)
        {
            // Format: KAMPAY|QRCodeId|ProductId|Timestamp
            return $"KAMPAY|{delivery.QRCodeId}|{delivery.ProductId}|{delivery.CreatedAt.Ticks}";
        }

        public async Task<ServiceResult<DeliveryQRCode>> ValidateQRCodeAsync(string qrCodeData)
        {
            try
            {
                // QR kod formatýný kontrol et
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

                // Firebase'den al
                var delivery = await _firebaseClient
                    .Child(QRCodesCollection)
                    .Child(qrCodeId)
                    .OnceSingleAsync<DeliveryQRCode>();

                if (delivery == null)
                {
                    return ServiceResult<DeliveryQRCode>.FailureResult("QR kod bulunamadý");
                }

                // Kontroller
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

                // Ürünü satýldý olarak iþaretle
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