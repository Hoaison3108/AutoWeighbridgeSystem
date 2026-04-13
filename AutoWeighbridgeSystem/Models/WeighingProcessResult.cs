namespace AutoWeighbridgeSystem.Models
{
    public class WeighingProcessResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public WeighingTicket Ticket { get; set; }
        public bool IsFirstWeighing { get; set; } // true: Cân lần 1, false: Cân lần 2
    }
}