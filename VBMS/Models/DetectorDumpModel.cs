namespace VBMS.Models
{
    public class DetectorDumpModel
    {
        public DateTime ReceivedAt { get; set; }
        public string RackId { get; set; } = string.Empty;
        public string BoardId { get; set; } = string.Empty;
        public double Temperature { get; set; }
        public double GasLevel { get; set; }
        public bool IsFire { get; set; }
        public string IsError { get; set; } = string.Empty;
    }
}