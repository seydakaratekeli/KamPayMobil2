using System;

namespace KamPay.Models
{
    public class UserProfile
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string ProfileImageUrl { get; set; }
        public DateTime MemberSince { get; set; }
    }
}