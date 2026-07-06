using astratech_apps_backend.DTOs.Iot;
using astratech_apps_backend.DTOs.Iot.Request;

namespace astratech_apps_backend.Repositories.Interfaces
{
    public interface IIotRepository
    {
        Task<string> CreateDeviceAsync(CreateDeviceRequest dto, string username);
        Task<bool> UpdateDeviceAsync(UpdateDeviceRequest dto, string username);
        Task<(IEnumerable<Device> list, int totalData)> GetAllDevicesAsync(GetAllDevicesRequest dto);
        Task<Device?> GetDeviceByIdAsync(string id);
        Task<bool> ToggleDeviceStatusAsync(string id, string status, string username);
        Task<bool> UpdateDeviceLocationAsync(string deviceId, int locationId, string username);

        Task<int> CreateLocationAsync(CreateLocationRequest dto, string username);
        Task<bool> UpdateLocationAsync(UpdateLocationRequest dto, string username);
        Task<bool> ToggleLocationStatusAsync(int id, string username);
        Task<IEnumerable<Location>> GetAllLocationsAsync(string? keyword, string? status);

        Task<bool> InsertTelemetryAsync(InsertTelemetryRequest dto);
        Task<IEnumerable<Device>> GetDashboardLatestAsync(string? username);
        Task<IEnumerable<GraphPoint>> GetDashboardGraphAsync(string deviceId, string period);

        Task<bool> AssignAccessAsync(string targetUsername, string deviceId, string createdBy);
        Task<bool> RemoveAccessAsync(string targetUsername, string deviceId);
        Task<bool> CheckAccessAsync(string username, string deviceId);

        Task<IEnumerable<Notification>> GetNotificationsAsync(string? username);
        Task<bool> MarkNotificationAsReadAsync(long notId);
        Task<bool> MarkNotificationsAsReadBulkAsync(List<long> ids);
        Task<bool> DeleteNotificationsBulkAsync(List<long> ids);
    }
}
