namespace astratech_apps_backend.DTOs.Iot
{
    public record Notification
    {
        public long Id { get; init; }
        public string DeviceId { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime TriggeredDate { get; init; }
    }
}
