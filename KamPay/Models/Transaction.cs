using System;
using KamPay.Models;
using System.Text.Json.Serialization; // Bu using ifadesini ekleyin

namespace KamPay.Models
{
    // Bir teklifin veya isteðin tüm yaþam döngüsünü takip eden ana model
    public class Transaction
    {
        public string TransactionId { get; set; } = Guid.NewGuid().ToString();

        // Ýlgili Ana Ürün Bilgileri
        public string ProductId { get; set; }
        public string ProductTitle { get; set; }
        public string ProductThumbnailUrl { get; set; }
        public ProductType Type { get; set; } // Satýþ, Takas, Baðýþ

        // Taraflar
        public string SellerId { get; set; } // Ürünü sunan kiþi
        public string SellerName { get; set; }
        public string BuyerId { get; set; }  // Teklifi yapan/isteði gönderen kiþi
        public string BuyerName { get; set; }

        // Durum ve Zaman Bilgileri
        public TransactionStatus Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        // YENÝ: Bu liste, QR kodlarýnýn durumunu takip etmek için kullanýlacak.
        [JsonIgnore] // Firebase'e bu alaný kaydetmemesi için ignore ediyoruz.
        public List<DeliveryQRCode> DeliveryQRCodes { get; set; } = new();

        public string StatusText
        {
            get
            {
                // Eðer tüm QR kodlarý kullanýldýysa, durumu "Tamamlandý" olarak göster.
                if (Status == TransactionStatus.Accepted && DeliveryQRCodes.Any() && DeliveryQRCodes.All(qr => qr.IsUsed))
                {
                    return "Tamamlandý";
                }

                // Aksi takdirde mevcut enum durumunu kullan.
                return Status switch
                {
                    TransactionStatus.Pending => "Onay Bekliyor",
                    TransactionStatus.Accepted => "Kabul Edildi",
                    TransactionStatus.Rejected => "Reddedildi",
                    TransactionStatus.Completed => "Tamamlandý",
                    TransactionStatus.Cancelled => "Ýptal Edildi",
                    _ => "Bilinmiyor"
                };
            }
        }

        public bool CanManageDelivery => Status == TransactionStatus.Accepted && !(DeliveryQRCodes.Any() && DeliveryQRCodes.All(qr => qr.IsUsed));
        public bool IsDeliveryCompleted => Status == TransactionStatus.Completed || (Status == TransactionStatus.Accepted && DeliveryQRCodes.Any() && DeliveryQRCodes.All(qr => qr.IsUsed));
        // Takas'a özel alanlar
        public string? OfferedProductId { get; set; } // Takas için teklif edilen ürünün ID'si
        public string? OfferedProductTitle { get; set; }
        public string? OfferMessage { get; set; } // Teklifle birlikte gönderilen mesaj
    }

    public enum TransactionStatus
    {
        Pending,      // Teklif yapýldý, satýcýnýn onayý bekliyor
        Accepted,     // Teklif kabul edildi, teslimat süreci bekleniyor
        Rejected,     // Teklif reddedildi
        Completed,    // Teslimat tamamlandý ve iþlem kapandý
        Cancelled     // Taraflardan biri iptal etti
    }
}