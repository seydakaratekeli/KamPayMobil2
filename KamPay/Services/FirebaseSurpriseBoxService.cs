using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;
using KamPay.Services;
namespace KamPay.Services;

public class FirebaseSurpriseBoxService : ISurpriseBoxService
{
    private readonly FirebaseClient _firebaseClient;
    private const string SurpriseBoxesCollection = "surprise_boxes";

    public FirebaseSurpriseBoxService()
    {
        _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
    }

    public async Task<ServiceResult<SurpriseBox>> CreateSurpriseBoxAsync(string productId, User donor)
    {
        try
        {
            var product = await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .OnceSingleAsync<Product>();

            if (product == null)
            {
                return ServiceResult<SurpriseBox>.FailureResult("Ürün bulunamadı");
            }

            var box = new SurpriseBox
            {
                DonorId = donor.UserId,
                DonorName = donor.FullName,
                ProductId = productId,
                ProductTitle = product.Title,
                ProductImage = product.ThumbnailUrl
            };

            await _firebaseClient
                .Child(SurpriseBoxesCollection)
                .Child(box.BoxId)
                .PutAsync(box);

            return ServiceResult<SurpriseBox>.SuccessResult(
                box,
                "Sürpriz kutu oluşturuldu! Rastgele bir öğrenci bu hediyeyi alacak 🎁"
            );
        }
        catch (Exception ex)
        {
            return ServiceResult<SurpriseBox>.FailureResult("Hata oluştu", ex.Message);
        }
    }

    public async Task<ServiceResult<SurpriseBox>> OpenRandomBoxAsync(string userId)
    {
        try
        {
            var availableBoxes = await GetAvailableBoxesAsync();

            if (!availableBoxes.Success || !availableBoxes.Data.Any())
            {
                return ServiceResult<SurpriseBox>.FailureResult("Şu an açılabilecek sürpriz kutu yok");
            }

            // Rastgele bir kutu seç
            var random = new Random();
            var selectedBox = availableBoxes.Data[random.Next(availableBoxes.Data.Count)];

            // Kutuyu aç
            selectedBox.IsOpened = true;
            selectedBox.RecipientId = userId;
            selectedBox.OpenedAt = DateTime.UtcNow;

            await _firebaseClient
                .Child(SurpriseBoxesCollection)
                .Child(selectedBox.BoxId)
                .PutAsync(selectedBox);

            return ServiceResult<SurpriseBox>.SuccessResult(
                selectedBox,
                $"Tebrikler! {selectedBox.ProductTitle} senin oldu! 🎉"
            );
        }
        catch (Exception ex)
        {
            return ServiceResult<SurpriseBox>.FailureResult("Hata oluştu", ex.Message);
        }
    }

    public async Task<ServiceResult<List<SurpriseBox>>> GetAvailableBoxesAsync()
    {
        try
        {
            var allBoxes = await _firebaseClient
                .Child(SurpriseBoxesCollection)
                .OnceAsync<SurpriseBox>();

            var availableBoxes = allBoxes
                .Select(b => b.Object)
                .Where(b => !b.IsOpened)
                .OrderBy(b => b.CreatedAt)
                .ToList();

            return ServiceResult<List<SurpriseBox>>.SuccessResult(availableBoxes);
        }
        catch (Exception ex)
        {
            return ServiceResult<List<SurpriseBox>>.FailureResult("Hata", ex.Message);
        }
    }
}