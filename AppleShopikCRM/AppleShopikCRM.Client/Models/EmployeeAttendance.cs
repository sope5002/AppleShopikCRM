using System;
using System.ComponentModel.DataAnnotations;

namespace AppleShopikCRM.Server.Models
{
    public class EmployeeAttendance
    {
        [Key]
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty; // ID користувача (з Identity)
        public string UserName { get; set; } = string.Empty; // ПІБ або логін працівника
        public int StoreId { get; set; } // ID магазину, де він працює

        public DateTime WorkDate { get; set; } // Дата робочого дня (без часу)

        public DateTime? ClockInTime { get; set; } // Точний час приходу
        public DateTime? ClockOutTime { get; set; } // Точний час закінчення роботи
    }
}