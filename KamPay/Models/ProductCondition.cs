/*
 * SİLİNECEK
 *using System;
using System.ComponentModel;

namespace KamPay.Models
{
    /// <summary>
    /// Ürün durumu enum sınıfı
    /// </summary>
    public enum ProductCondition
    {
        [Description("Yeni Gibi - Sıfır ayarında, hiç kullanılmamış")]
        YeniGibi = 0,
        
        [Description("Çok İyi - Az kullanılmış, kusursuz durumda")]
        CokIyi = 1,
        
        [Description("İyi - Normal kullanılmış, iyi durumda")]
        Iyi = 2,
        
        [Description("Orta - Kullanım izleri var ama çalışıyor")]
        Orta = 3,
        
        [Description("Kullanılabilir - Kullanılabilir ama eskimiş")]
        Kullanilabilir = 4
    }
    
    /// <summary>
    /// ProductCondition için yardımcı metodlar
    /// </summary>
    public static class ProductConditionExtensions
    {
        /// <summary>
        /// Ürün durumunu Türkçe metne çevirir
        /// </summary>
        public static string ToDisplayString(this ProductCondition condition)
        {
            return condition switch
            {
                ProductCondition.YeniGibi => "Yeni Gibi",
                ProductCondition.CokIyi => "Çok İyi",
                ProductCondition.Iyi => "İyi",
                ProductCondition.Orta => "Orta",
                ProductCondition.Kullanilabilir => "Kullanılabilir",
                _ => "Belirtilmemiş"
            };
        }
        
        /// <summary>
        /// Ürün durumu açıklamasını döndürür
        /// </summary>
        public static string GetDescription(this ProductCondition condition)
        {
            return condition switch
            {
                ProductCondition.YeniGibi => "Sıfır ayarında, hiç kullanılmamış veya çok az kullanılmış",
                ProductCondition.CokIyi => "Az kullanılmış, görünür kusur yok, mükemmel durumda",
                ProductCondition.Iyi => "Normal kullanılmış, hafif kullanım izleri olabilir",
                ProductCondition.Orta => "Kullanım izleri var ama tam çalışır durumda",
                ProductCondition.Kullanilabilir => "Eskimiş ama hala kullanılabilir",
                _ => "Durum belirtilmemiş"
            };
        }
        
        /// <summary>
        /// Duruma göre emoji döndürür
        /// </summary>
        public static string GetEmoji(this ProductCondition condition)
        {
            return condition switch
            {
                ProductCondition.YeniGibi => "✨",
                ProductCondition.CokIyi => "⭐",
                ProductCondition.Iyi => "👍",
                ProductCondition.Orta => "👌",
                ProductCondition.Kullanilabilir => "✅",
                _ => "❓"
            };
        }
        
        /// <summary>
        /// Duruma göre renk kodu döndürür
        /// </summary>
        public static string GetColorCode(this ProductCondition condition)
        {
            return condition switch
            {
                ProductCondition.YeniGibi => "#4CAF50",  // Yeşil
                ProductCondition.CokIyi => "#8BC34A",    // Açık yeşil
                ProductCondition.Iyi => "#FFC107",       // Sarı
                ProductCondition.Orta => "#FF9800",      // Turuncu
                ProductCondition.Kullanilabilir => "#FF5722", // Kırmızımsı
                _ => "#757575"                           // Gri
            };
        }
        
        /// <summary>
        /// Tüm durumları döndürür
        /// </summary>
        public static ProductCondition[] GetAllConditions()
        {
            return new[]
            {
                ProductCondition.YeniGibi,
                ProductCondition.CokIyi,
                ProductCondition.Iyi,
                ProductCondition.Orta,
                ProductCondition.Kullanilabilir
            };
        }
    }
}
 * */