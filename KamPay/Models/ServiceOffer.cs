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
    Education = 0,      // �zel ders, ders notu
    Technical = 1,      // Bilgisayar tamiri, kod yard�m�
    Cooking = 2,        // Yemek yapma
    Childcare = 3,      // �ocuk bak�m�
    PetCare = 4,        // Evcil hayvan bak�m�
    Translation = 5,    // �eviri
    Moving = 6,         // Ta��ma yard�m�
    Other = 7           // Di�er
}

// --- YEN� EKLENEN MODELLER ---

public class ServiceRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string ServiceId { get; set; }
    public string ServiceTitle { get; set; } // Bildirimler ve UI i�in
    public string ProviderId { get; set; }   // Hizmeti sunan ki�i
    public string RequesterId { get; set; }  // Hizmeti talep eden ki�i
    public string RequesterName { get; set; }

    public decimal Price { get; set; } = 0; // Hizmet fiyat� (�rne�in 150 TL)
    public string Currency { get; set; } = "TRY";

    // ServiceRequest.cs i�inde:
    public ServicePaymentStatus PaymentStatus { get; set; } = ServicePaymentStatus.None;
    public string? PaymentSimulationId { get; set; }
    public PaymentMethodType? PaymentMethod { get; set; } = PaymentMethodType.None;
    public decimal? QuotedPrice { get; set; }   // Kabul an�nda kilitlenmi� fiyat (istersen)

    // --- YEN� EKLENECEK PROPERTY ---
    public int TimeCreditValue { get; set; } // ��lemin yap�ld��� andaki kredi de�eri

    public string Message { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.Pending;
    public DateTime? CompletedAt { get; set; } // Hizmetin tamamland��� zaman� tutmak i�in
}

public enum ServiceRequestStatus
{
    Pending = 0,    // Beklemede
    Accepted = 1,   // Kabul Edildi
    Declined = 2,   // Reddedildi
    Completed = 3   // Tamamland�
}