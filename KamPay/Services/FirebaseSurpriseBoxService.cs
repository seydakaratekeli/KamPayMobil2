using Firebase.Database;
using KamPay.Helpers;
using KamPay.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace KamPay.Services
{
    // ISurpriseBoxService arayüzünü uyguladığını belirtiyoruz
    public class FirebaseSurpriseBoxService : ISurpriseBoxService
    {
        private readonly FirebaseClient _firebaseClient;
        // Diğer servislerle konuşmak için dependency injection kullanıyoruz
        private readonly IUserProfileService _userProfileService;
        private readonly IProductService _productService;
        private const int BoxCost = 100; // Kutunun maliyetini bir sabit olarak tanımlıyoruz

        // Constructor'ı (kurucu metot) DI uyumlu hale getiriyoruz
        public FirebaseSurpriseBoxService(IUserProfileService userProfileService, IProductService productService)
        {
            _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
            _userProfileService = userProfileService;
            _productService = productService;
        }

        // ISurpriseBoxService arayüzünün gerektirdiği, eksik olan metot
        public async Task<ServiceResult<Product>> RedeemSurpriseBoxAsync(string userId)
        {
            try
            {
                // 1. Kullanıcının puanını kontrol et
                var userStatsResult = await _userProfileService.GetUserStatsAsync(userId);
                if (!userStatsResult.Success || userStatsResult.Data.Points < BoxCost)
                {
                    return ServiceResult<Product>.FailureResult("Yetersiz Puan!", $"Bu işlem için {BoxCost} puana ihtiyacınız var.");
                }

                // 2. Bağış olarak işaretlenmiş, uygun ürünleri bul
                var allProductsResult = await _productService.GetProductsAsync();
                if (!allProductsResult.Success || allProductsResult.Data == null)
                {
                    return ServiceResult<Product>.FailureResult("Hata", "Ürünler alınamadı.");
                }

                var availableDonations = allProductsResult.Data
                    .Where(p => p.Type == ProductType.Bagis && !p.IsSold && !p.IsReserved)
                    .ToList();

                if (availableDonations.Count == 0)
                {
                    return ServiceResult<Product>.FailureResult("Ürün Yok", "Şu anda sürpriz kutusu için uygun bir ürün bulunmuyor.");
                }

                // 3. Rastgele bir ürün seç
                var random = new Random();
                var surpriseProduct = availableDonations[random.Next(availableDonations.Count)];

                // 4. Puanı düş ve ürünü kullanıcıya ata (sahibini güncelle)
                var pointsDeducted = await _userProfileService.AddPointsAsync(userId, -BoxCost, "Sürpriz Kutu açıldı");

                if (!pointsDeducted.Success)
                {
                    return ServiceResult<Product>.FailureResult("Hata", "Puan düşülürken bir sorun oluştu.");
                }

                // Ürünün sahibini, kutuyu açan kullanıcı olarak güncelle
                var ownerUpdated = await _productService.UpdateProductOwnerAsync(surpriseProduct.ProductId, userId);
                if (!ownerUpdated.Success)
                {
                    // Eğer ürün sahibi güncellenemezse, bir hata olduğunu belirt.
                    // İleri seviye bir implementasyonda burada kullanıcının puanı iade edilebilir.
                    return ServiceResult<Product>.FailureResult("Hata", "Ürün sahipliği güncellenirken bir sorun oluştu.");
                }

                // 5. Başarılı sonucu ve kazanılan ürünü döndür
                return ServiceResult<Product>.SuccessResult(surpriseProduct, "Tebrikler!");
            }
            catch (Exception ex)
            {
                return ServiceResult<Product>.FailureResult("Beklenmedik Hata", ex.Message);
            }
        }
    }
}