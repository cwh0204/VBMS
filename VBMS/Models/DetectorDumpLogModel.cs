namespace VBMS.Models
{
    public class DetectorDumpLogModel
    {
        public DateTime ReceivedAt { get; set; }
        public int Bay { get; set; }
        public int Level { get; set; }
        public int Row { get; set; }
        public double Temperature { get; set; }
        public bool IsFire { get; set; }
        public bool IsError { get; set; }
    }
}