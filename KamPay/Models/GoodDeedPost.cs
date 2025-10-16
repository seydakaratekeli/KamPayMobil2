using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Models;
using Newtonsoft.Json;

namespace KamPay.Models;


public class GoodDeedPost
{
    public string PostId { get; set; }
    public string UserId { get; set; }
    public string UserName { get; set; }
    public PostType Type { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    public string? ContactInfo { get; set; }

    [JsonIgnore] // <-- Bu attribute, özelliðin Firebase'e kaydedilmesini engeller.
    public bool IsOwner { get; set; }

    public GoodDeedPost()
    {
        PostId = Guid.NewGuid().ToString();
        CreatedAt = DateTime.UtcNow;
        IsActive = true;
        LikeCount = 0;
        CommentCount = 0;
    }
}

public enum PostType
{
    HelpRequest = 0,   // Yardým talebi
    Announcement = 1,  // Duyuru
    ThankYou = 2,      // Teþekkür
    Volunteer = 3      // Gönüllü arýyorum
}