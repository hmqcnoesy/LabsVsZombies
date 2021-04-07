using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace LabsVsZombies.Models
{
    public class BackgroundTask
    {
        public decimal BackgroundId { get; set; }
        public string SessionUser { get; set; }
        public decimal? ScheduleId { get; set; }
        public string Parameter { get; set; }
        public bool Active { get; set; }
        public string TaskType { get; set; }
    }
}