namespace VBMS.Models
{
    public class EventLogModel
    {
        public int Row { get; set; }
        public int Bay { get; set; }
        public int Level { get; set; }
        public string Content { get; set; } = string.Empty;
        public string DateTime { get; set; } = string.Empty;
    }
}