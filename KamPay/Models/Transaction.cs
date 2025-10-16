using System;
using KamPay.Models;
using System.Text.Json.Serialization; // Bu using ifadesini ekleyin

namespace KamPay.Models
{
    // Bir teklifin veya iste�in t�m ya�am d�ng�s�n� takip eden ana model
    public class Transaction
    {
        public string TransactionId { get; set; } = Guid.NewGuid().ToString();

        // �lgili Ana �r�n Bilgileri
        public string ProductId { get; set; }
        public string ProductTitle { get; set; }
        public string ProductThumbnailUrl { get; set; }
        public ProductType Type { get; set; } // Sat��, Takas, Ba���

        // Taraflar
        public string SellerId { get; set; } // �r�n� sunan ki�i
        public string SellerName { get; set; }
        public string BuyerId { get; set; }  // Teklifi yapan/iste�i g�nderen ki�i
        public string BuyerName { get; set; }

        // Durum ve Zaman Bilgileri
        public TransactionStatus Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        // YEN�: Bu liste, QR kodlar�n�n durumunu takip etmek i�in kullan�lacak.
        [JsonIgnore] // Firebase'e bu alan� kaydetmemesi i�in ignore ediyoruz.
        public List<DeliveryQRCode> DeliveryQRCodes { get; set; } = new();

        public string StatusText
        {
            get
            {
                // E�er t�m QR kodlar� kullan�ld�ysa, durumu "Tamamland�" olarak g�ster.
                if (Status == TransactionStatus.Accepted && DeliveryQRCodes.Any() && DeliveryQRCodes.All(qr => qr.IsUsed))
                {
                    return "Tamamland�";
                }

                // Aksi takdirde mevcut enum durumunu kullan.
                return Status switch
                {
                    TransactionStatus.Pending => "Onay Bekliyor",
                    TransactionStatus.Accepted => "Kabul Edildi",
                    TransactionStatus.Rejected => "Reddedildi",
                    TransactionStatus.Completed => "Tamamland�",
                    TransactionStatus.Cancelled => "�ptal Edildi",
                    _ => "Bilinmiyor"
                };
            }
        }

        public bool CanManageDelivery => Status == TransactionStatus.Accepted && !(DeliveryQRCodes.Any() && DeliveryQRCodes.All(qr => qr.IsUsed));
        public bool IsDeliveryCompleted => Status == TransactionStatus.Completed || (Status == TransactionStatus.Accepted && DeliveryQRCodes.Any() && DeliveryQRCodes.All(qr => qr.IsUsed));
        // Takas'a �zel alanlar
        public string? OfferedProductId { get; set; } // Takas i�in teklif edilen �r�n�n ID'si
        public string? OfferedProductTitle { get; set; }
        public string? OfferMessage { get; set; } // Teklifle birlikte g�nderilen mesaj
    }

    public enum TransactionStatus
    {
        Pending,      // Teklif yap�ld�, sat�c�n�n onay� bekliyor
        Accepted,     // Teklif kabul edildi, teslimat s�reci bekleniyor
        Rejected,     // Teklif reddedildi
        Completed,    // Teslimat tamamland� ve i�lem kapand�
        Cancelled     // Taraflardan biri iptal etti
    }
}