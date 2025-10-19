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

        // METODU TAMAMEN GÜNCELLEYİN
        public async Task<ServiceResult<Product>> RedeemSurpriseBoxAsync(string userId)
        {
            try
            {
                // 1. Kullanıcının puanını kontrol et (Bu kısım aynı)
                var userStatsResult = await _userProfileService.GetUserStatsAsync(userId);
                if (!userStatsResult.Success || userStatsResult.Data.Points < BoxCost)
                {
                    return ServiceResult<Product>.FailureResult("Yetersiz Puan!", $"Bu işlem için {BoxCost} puana ihtiyacınız var.");
                }

                // 2. DAHA VERİMLİ SORGULAMA: 
                // Sadece Sürpriz Kutu için işaretlenmiş ürünleri doğrudan veritabanından çek.
                var surpriseBoxProducts = await _firebaseClient
                    .Child(Constants.ProductsCollection)
                    .OnceAsync<Product>(); // Firebase'den verileri bir kere çek

                // Bellekte filtrele: Sürpriz kutusu için, satılmamış ve kullanıcının kendisine ait olmayan ürünler
                var availableDonations = surpriseBoxProducts
                    .Where(p => p.Object.Type == ProductType.Bagis &&
                                p.Object.IsForSurpriseBox &&
                                !p.Object.IsSold &&
                                p.Object.UserId != userId)
                    .Select(p => { p.Object.ProductId = p.Key; return p.Object; })
                    .ToList();

                if (availableDonations.Count == 0)
                {
                    return ServiceResult<Product>.FailureResult("Ürün Yok", "Şu anda sürpriz kutusu için uygun bir ürün bulunmuyor.");
                }

                // 3. Rastgele bir ürün seç (Bu kısım aynı)
                var random = new Random();
                var surpriseProduct = availableDonations[random.Next(availableDonations.Count)];

                // 4. Puanı düş ve ürünü kullanıcıya ata (Bu kısım aynı)
                var pointsDeducted = await _userProfileService.AddPointsAsync(userId, -BoxCost, "Sürpriz Kutu açıldı");

                if (!pointsDeducted.Success)
                {
                    return ServiceResult<Product>.FailureResult("Hata", "Puan düşülürken bir sorun oluştu.");
                }

                // Ürünün sahibini, kutuyu açan kullanıcı olarak güncelle
                var ownerUpdated = await _productService.UpdateProductOwnerAsync(surpriseProduct.ProductId, userId);
                if (!ownerUpdated.Success)
                {
                    // Hata durumunda puanı iade et (daha güvenli bir yaklaşım)
                    await _userProfileService.AddPointsAsync(userId, BoxCost, "Sürpriz Kutu hatası (iade)");
                    return ServiceResult<Product>.FailureResult("Hata", "Ürün sahipliği güncellenemedi. Puanınız iade edildi.");
                }

                // 5. Başarılı sonucu döndür (Bu kısım aynı)
                return ServiceResult<Product>.SuccessResult(surpriseProduct, "Tebrikler! Sürpriz kutusundan bu ürün çıktı!");
            }
            catch (Exception ex)
            {
                return ServiceResult<Product>.FailureResult("Beklenmedik Hata", ex.Message);
            }
        }
    }
}