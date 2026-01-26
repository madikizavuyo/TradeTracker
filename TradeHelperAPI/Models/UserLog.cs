// UserLog.cs – Records user/system actions for auditing
using System;

namespace TradeHelper.Models
{
    public class UserLog
    {
        public int Id { get; set; }
        public required string Email { get; set; }
        public required string Action { get; set; }
        public DateTime Timestamp { get; set; }
    }
}