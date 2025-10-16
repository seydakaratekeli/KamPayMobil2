using CommunityToolkit.Mvvm.Messaging.Messages;

namespace KamPay.Models.Messages
{
    // Bu s�n�f, QR kod tarand���nda g�nderilecek mesaj� temsil eder.
    // ��inde taranan QR kodun metnini (string) ta��r.
    public class QRCodeScannedMessage : ValueChangedMessage<string>
    {
        public QRCodeScannedMessage(string value) : base(value)
        {
        }
    }
}