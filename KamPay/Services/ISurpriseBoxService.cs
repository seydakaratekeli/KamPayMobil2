using KamPay.Models;
using System.Threading.Tasks;

namespace KamPay.Services
{
    public interface ISurpriseBoxService
    {
        // Bu aray�z art�k sadece bu tek metodu i�ermeli
        Task<ServiceResult<Product>> RedeemSurpriseBoxAsync(string userId);
    }
}