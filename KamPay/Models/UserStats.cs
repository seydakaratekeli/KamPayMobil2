namespace KamPay.Models;
// Kullanýcý istatistikleri
public class UserStats
{
    public string UserId { get; set; }
    public int TotalProducts { get; set; }
    public int ActiveProducts { get; set; }
    public int SoldProducts { get; set; }
    public int DonatedProducts { get; set; }
    public int PurchasedProducts { get; set; }
    public int TotalViews { get; set; }
    public int TotalFavorites { get; set; }
    public int DonationPoints { get; set; }
    public int TrustScore { get; set; }
    public DateTime MemberSince { get; set; }
    public DateTime? LastActivityAt { get; set; }

    public UserStats()
    {
        TrustScore = 100;
        DonationPoints = 0;
        MemberSince = DateTime.UtcNow;
    }
}
