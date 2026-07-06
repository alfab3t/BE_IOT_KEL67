namespace astratech_apps_backend.DTOs.Iot.Response
{
    public record GetDevicesResponse
    {
        public IEnumerable<Device> Data { get; init; } = [];
        public int TotalData { get; init; }
        public int TotalHalaman { get; init; }
    }
}
