using System.Threading.Tasks;
using KamPay.Models;

namespace KamPay.Services
{
    public interface IAuthenticationService
    {
        /// <summary>
        /// Yeni kullanýcý kaydý yapar ve doðrulama kodu gönderir
        /// </summary>
        Task<ServiceResult<User>> RegisterAsync(RegisterRequest request);

        /// <summary>
        /// Kullanýcý giriþi yapar
        /// </summary>
        Task<ServiceResult<User>> LoginAsync(LoginRequest request);

        /// <summary>
        /// E-posta doðrulama kodu gönderir
        /// </summary>
        Task<ServiceResult<bool>> SendVerificationCodeAsync(string email);

        /// <summary>
        /// E-posta doðrulama kodunu kontrol eder
        /// </summary>
        Task<ServiceResult<bool>> VerifyEmailAsync(VerificationRequest request);

        /// <summary>
        /// Kayýt isteðini doðrular
        /// </summary>
        ValidationResult ValidateRegistration(RegisterRequest request);

        /// <summary>
        /// Giriþ isteðini doðrular
        /// </summary>
        ValidationResult ValidateLogin(LoginRequest request);

        /// <summary>
        /// Kullanýcý çýkýþý yapar
        /// </summary>
        Task<ServiceResult<bool>> LogoutAsync();

        /// <summary>
        /// Þu anki kullanýcýyý getirir
        /// </summary>
        Task<User> GetCurrentUserAsync();

        /// <summary>
        /// Kullanýcýnýn giriþ yapýp yapmadýðýný kontrol eder
        /// </summary>
        bool IsUserLoggedIn();
    }
}