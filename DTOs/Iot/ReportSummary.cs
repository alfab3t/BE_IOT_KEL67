namespace astratech_apps_backend.DTOs.Iot
{
    public class ReportSummary
    {
        // Device stats
        public int TotalDevice { get; set; }
        public int DeviceOnline { get; set; }
        public int DeviceOffline { get; set; }
        public int TotalUsers { get; set; }


        // Temperature (°C)
        public decimal AvgTemperature { get; set; }
        public decimal MaxTemperature { get; set; }
        public decimal MinTemperature { get; set; }

        // Humidity (%)
        public decimal AvgHumidity { get; set; }
        public decimal MaxHumidity { get; set; }
        public decimal MinHumidity { get; set; }

        // Power (kWh)
        public decimal TotalPowerKwh { get; set; }

        // Period label
        public string Period { get; set; } = "today";
    }
}
