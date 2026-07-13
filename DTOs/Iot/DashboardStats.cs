namespace astratech_apps_backend.DTOs.Iot
{
    public class DashboardStats
    {
        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int OfflineDevices { get; set; }
        public int TotalUsers { get; set; }
        public decimal AvgSuhu { get; set; }
        public decimal AvgHumi { get; set; }
        public decimal AvgPower { get; set; }
        public decimal EstKWh { get; set; }
    }
}
