namespace astratech_apps_backend.DTOs.Iot
{
    public record Location
    {
        public int Id { get; init; }
        public string Gedung { get; init; } = string.Empty;
        public string Lantai { get; init; } = string.Empty;
        public string Ruangan { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public int ActiveDeviceCount { get; init; } = 0;
    }
}
