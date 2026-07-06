namespace astratech_apps_backend.DTOs.Iot
{
    public record Device
    {
        public string Id { get; init; } = string.Empty;
        public string MacAddress { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public decimal? LimitMin { get; init; }
        public decimal? LimitMax { get; init; }
        public string? Photo { get; init; }
        public int? LocationId { get; init; }
        public string? Gedung { get; init; }
        public string? Lantai { get; init; }
        public string? Ruangan { get; init; }
        public long RowNumber { get; init; } = 0;
    }
}
