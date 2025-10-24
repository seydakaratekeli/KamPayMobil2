using Newtonsoft.Json;


namespace KamPay.Models;

public class ServiceOffer
{
    public string ServiceId { get; set; }
    public string ProviderId { get; set; }
    public string ProviderName { get; set; }
    public ServiceCategory Category { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public int TimeCredits { get; set; } // Saat cinsinden
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsAvailable { get; set; }
    public List<string> Tags { get; set; }
    [JsonProperty("price")]
    public decimal Price { get; set; } = 0;

    public ServiceOffer()
    {
        ServiceId = Guid.NewGuid().ToString();
        CreatedAt = DateTime.UtcNow;
        IsAvailable = true;
        Tags = new List<string>();
    }
}

public enum ServiceCategory
{
    Education = 0,      // Özel ders, ders notu
    Technical = 1,      // Bilgisayar tamiri, kod yardýmý
    Cooking = 2,        // Yemek yapma
    Childcare = 3,      // Çocuk bakýmý
    PetCare = 4,        // Evcil hayvan bakýmý
    Translation = 5,    // Çeviri
    Moving = 6,         // Taþýma yardýmý
    Other = 7           // Diðer
}

// --- YENÝ EKLENEN MODELLER ---

public class ServiceRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string ServiceId { get; set; }
    public string ServiceTitle { get; set; } // Bildirimler ve UI için
    public string ProviderId { get; set; }   // Hizmeti sunan kiþi
    public string RequesterId { get; set; }  // Hizmeti talep eden kiþi
    public string RequesterName { get; set; }

    public decimal Price { get; set; } = 0; // Hizmet fiyatý (örneðin 150 TL)
    public string Currency { get; set; } = "TRY";

    // ServiceRequest.cs içinde:
    public ServicePaymentStatus PaymentStatus { get; set; } = ServicePaymentStatus.None;
    public string? PaymentSimulationId { get; set; }
    public PaymentMethodType? PaymentMethod { get; set; } = PaymentMethodType.None;
    public decimal? QuotedPrice { get; set; }   // Kabul anýnda kilitlenmiþ fiyat (istersen)

    // --- YENÝ EKLENECEK PROPERTY ---
    public int TimeCreditValue { get; set; } // Ýþlemin yapýldýðý andaki kredi deðeri

    public string Message { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.Pending;
    public DateTime? CompletedAt { get; set; } // Hizmetin tamamlandýðý zamaný tutmak için
}

public enum ServiceRequestStatus
{
    Pending = 0,    // Beklemede
    Accepted = 1,   // Kabul Edildi
    Declined = 2,   // Reddedildi
    Completed = 3   // Tamamlandý
}