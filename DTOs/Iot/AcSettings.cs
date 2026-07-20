namespace astratech_apps_backend.DTOs.Iot
{
    public record AcSettings
    {
        public int Temperature { get; init; } = 22;
        public string Mode { get; init; } = "Cool";
        public string FanSpeed { get; init; } = "Auto";
        public string SwingMode { get; init; } = "Auto";
    }
}
