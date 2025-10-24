using KamPay.Models;
using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace KamPay.Models
{
    // 🔹 Bir ürün satışı, takası veya bağışı sürecini takip eden ana model
    public class Transaction
    {
        public string TransactionId { get; set; } = Guid.NewGuid().ToString();

        // 🔹 İlgili Ana Ürün Bilgileri
        public string ProductId { get; set; }
        public string ProductTitle { get; set; }
        public string ProductThumbnailUrl { get; set; }
        public ProductType Type { get; set; } // Satış, Takas, Bağış

        // 🔹 Taraflar
        public string SellerId { get; set; } // Ürünü sunan kişi
        public string SellerName { get; set; }
        public string BuyerId { get; set; }  // Teklifi yapan/isteği gönderen kişi
        public string BuyerName { get; set; }

        // 🔹 Ödeme Durumu
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

        // 🆕 Yeni eklenen ödeme bilgileri (simülasyon desteği için)
        public PaymentMethodType PaymentMethod { get; set; } = PaymentMethodType.None; // CardSim, BankTransferSim vb.
        public string? PaymentSimulationId { get; set; } // OTP ile eşleşecek ID
        public decimal QuotedPrice { get; set; } // Satış anındaki kilitli fiyat
        public string Currency { get; set; } = "TRY"; // Para birimi
        public DateTime? PaymentCompletedAt { get; set; } // Ödeme tamamlanma zamanı

        // 🔹 Durum ve Zaman Bilgileri
        public TransactionStatus Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // 🔹 QR Kod Teslimat Takibi
        [JsonIgnore]
        public List<DeliveryQRCode> DeliveryQRCodes { get; set; } = new();

        // 🔹 Görsel durum metni
        public string StatusText
        {
            get
            {
                if (Status == TransactionStatus.Accepted && DeliveryQRCodes.Any() && DeliveryQRCodes.All(qr => qr.IsUsed))
                    return "Tamamlandı";

                return Status switch
                {
                    TransactionStatus.Pending => "Onay Bekliyor",
                    TransactionStatus.Accepted => "Kabul Edildi",
                    TransactionStatus.Rejected => "Reddedildi",
                    TransactionStatus.Completed => "Tamamlandı",
                    TransactionStatus.Cancelled => "İptal Edildi",
                    _ => "Bilinmiyor"
                };
            }
        }

        public bool CanManageDelivery =>
            Type == ProductType.Takas &&
            Status == TransactionStatus.Accepted &&
            !(DeliveryQRCodes.Any() && DeliveryQRCodes.All(qr => qr.IsUsed));

        public bool IsDeliveryCompleted =>
            Status == TransactionStatus.Completed ||
            (Status == TransactionStatus.Accepted && DeliveryQRCodes.Any() && DeliveryQRCodes.All(qr => qr.IsUsed));

        // 🔹 Takas'a özel alanlar
        public string? OfferedProductId { get; set; }
        public string? OfferedProductTitle { get; set; }
        public string? OfferMessage { get; set; }
    }

    // 🔸 İşlem Durumu
    public enum TransactionStatus
    {
        Pending,      // Teklif yapıldı, satıcının onayı bekliyor
        Accepted,     // Teklif kabul edildi, teslimat süreci bekleniyor
        Rejected,     // Teklif reddedildi
        Completed,    // Teslimat tamamlandı ve işlem kapandı
        Cancelled     // Taraflardan biri iptal etti
    }

    // 🔸 Ödeme Durumu
    public enum PaymentStatus
    {
        Pending, // Ödeme bekleniyor
        Paid,    // Ödendi (simülasyon)
        Failed   // Başarısız
    }

    // 🆕 Yeni: Ödeme Türleri (CardSim, EFTSim vs.)
    public enum PaymentMethodType
    {
        None,
        CardSim,          // Kart ile ödeme simülasyonu
        BankTransferSim,  // EFT / Havale simülasyonu
        WalletSim         // Cüzdan / bakiye simülasyonu (isteğe bağlı)
    }
}
