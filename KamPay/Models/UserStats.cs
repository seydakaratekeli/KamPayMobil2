using System;

namespace KamPay.Models
{
    public class UserStats
    {
        public string UserId { get; set; }

        /// <summary>
        /// Kullan�c�n�n oyunla�t�rma sistemiyle kazand��� toplam puan.
        /// </summary>
        public int Points { get; set; }

        /// <summary>
        /// Ba�ar�yla tamamlanan takas veya sat�� say�s�.
        /// </summary>
        public int CompletedTrades { get; set; }

        /// <summary>
        /// Kullan�c�n�n yapt��� toplam ba��� say�s�. (Rozet i�in)
        /// </summary>
        public int DonatedProducts { get; set; }

        /// <summary>
        /// Kullan�c�n�n payla�t��� (ilan a�t���) toplam �r�n say�s�. (Rozet i�in)
        /// </summary>
        public int TotalProducts { get; set; }

        /// <summary>
        /// Kullan�c�n�n sat�n ald��� veya takasla edindi�i toplam �r�n say�s�. (Rozet i�in)
        /// </summary>
        public int PurchasedProducts { get; set; }

        // Bu alanlar� eski kodunuzda g�rd�m, projenizin ihtiyac�na g�re kalabilirler.
        public int ItemsShared { get; set; }
        public int DonationsMade { get; set; }

        public int TimeCredits { get; set; } = 0; // Ba�lang�� de�eri 0

    }
}