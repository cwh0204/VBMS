using System.Collections.Generic;

namespace VBMS.Models
{
    public class FdsOptions
    {
        public List<LaneOption> Lanes { get; set; } = new();
    }

    public class LaneOption
    {
        public int LaneNumber { get; set; }
        public int TargetBay { get; set; }
        public int TargetLevel { get; set; }
        public List<string> BoardIds { get; set; } = new();
    }
}