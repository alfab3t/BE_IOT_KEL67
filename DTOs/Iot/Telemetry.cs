namespace astratech_apps_backend.DTOs.Iot
{
    public record Telemetry
    {
        public long Id { get; init; }
        public string DeviceId { get; init; } = string.Empty;
        public decimal Value { get; init; }
        public string Type { get; init; } = string.Empty;
        public DateTime RecordedDate { get; init; }
    }
}
