namespace astratech_apps_backend.DTOs.Iot.Request
{
    public record GetAllDevicesRequest
    {
        public string? SearchKeyword { get; init; }
        public string? Category { get; init; }
        public string? Status { get; init; }
        public int PageNumber { get; init; } = 1;
        public int PageSize { get; init; } = 10;
    }
}
