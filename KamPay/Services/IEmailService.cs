using System.Threading.Tasks;

namespace KamPay.Services
{
    public interface IEmailService
    {
        /// <summary>
        /// Doðrulama e-postasý gönderir. True dönerse gönderim baþarýlýdýr.
        /// </summary>
        Task<bool> SendVerificationEmailAsync(string toEmail, string verificationCode);
    }
}
