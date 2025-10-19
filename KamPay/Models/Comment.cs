﻿// KamPay/Models/Comment.cs

namespace KamPay.Models
{
    public class Comment
    {
        public string CommentId { get; set; } = Guid.NewGuid().ToString();
        public string PostId { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string UserProfileImageUrl { get; set; } // Bu alanı profil servisinden alacağız
        public string Text { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}