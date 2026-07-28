using astratech_apps_backend.DTOs.Iot;
using astratech_apps_backend.DTOs.Iot.Request;
using astratech_apps_backend.Helpers;
using astratech_apps_backend.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace astratech_apps_backend.Repositories.Implementations
{
    public class IotRepository(DatabaseConfig dbConfig, ILogger<IotRepository> logger) : IIotRepository
    {
        private readonly string _conn = dbConfig.ConnectionStringSIA;
        private readonly ILogger<IotRepository> _logger = logger;


        public async Task<string> CreateDeviceAsync(CreateDeviceRequest dto, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new DynamicParameters();
                parameters.Add("@dev_id", dto.Id);
                parameters.Add("@dev_mac_address", dto.MacAddress);
                parameters.Add("@dev_name", dto.Name);
                parameters.Add("@dev_category", dto.Category);
                parameters.Add("@dev_limit_min", dto.LimitMin);
                parameters.Add("@dev_limit_max", dto.LimitMax);
                parameters.Add("@dev_photo", dto.Photo);
                parameters.Add("@loc_id", dto.LocationId);
                parameters.Add("@CreatedBy", username);

                var result = await conn.ExecuteScalarAsync<string>("IOT_SP_Device_Create", parameters, commandType: CommandType.StoredProcedure);
                return result ?? dto.Id ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan device baru: {Id} | User: {Username}", dto.Id, username);
                return string.Empty;
            }
        }

        public async Task<bool> UpdateDeviceAsync(UpdateDeviceRequest dto, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new DynamicParameters();
                parameters.Add("@dev_id", dto.Id);
                parameters.Add("@dev_name", dto.Name);
                parameters.Add("@dev_category", dto.Category);
                parameters.Add("@dev_limit_min", dto.LimitMin);
                parameters.Add("@dev_limit_max", dto.LimitMax);
                parameters.Add("@dev_photo", dto.Photo);
                parameters.Add("@UpdatedBy", username);

                var rows = await conn.ExecuteScalarAsync<int>("IOT_SP_Device_Update", parameters, commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengupdate device: {Id} | User: {Username}", dto.Id, username);
                return false;
            }
        }

        public async Task<(IEnumerable<Device> list, int totalData)> GetAllDevicesAsync(GetAllDevicesRequest dto)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    Keyword = dto.SearchKeyword,
                    dto.Category,
                    dto.Status,
                    Halaman = dto.PageNumber,
                    Limit = dto.PageSize
                };

                var result = await conn.QueryAsync<dynamic>("IOT_SP_Device_GetData", parameters, commandType: CommandType.StoredProcedure);
                var list = new List<Device>();
                int totalData = 0;

                if (result.Any())
                {
                    totalData = (int)result.First().Count;
                    list = [.. result.Select(row => new Device
                    {
                        Id = (string)row.dev_id,
                        MacAddress = (string)row.dev_mac_address,
                        Name = (string)row.dev_name,
                        Category = (string)row.dev_category,
                        Status = (string)row.dev_status,
                        LimitMin = (decimal?)row.dev_limit_min,
                        LimitMax = (decimal?)row.dev_limit_max,
                        Photo = (string)row.dev_photo,
                        IsActive = (bool)row.dev_is_active,
                        LocationId = (int?)row.loc_id,
                        Gedung = (string)row.loc_gedung,
                        Lantai = (string)row.loc_lantai,
                        Ruangan = (string)row.loc_ruangan,
                        RowNumber = (long)row.rownum
                    })];
                }

                return (list, totalData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data device.");
                return ([], 0);
            }
        }

        public async Task<Device?> GetDeviceByIdAsync(string id)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var row = await conn.QueryFirstOrDefaultAsync<dynamic>("IOT_SP_Device_Detail", new { Id = id }, commandType: CommandType.StoredProcedure);
                if (row == null) return null;

                return new Device
                {
                    Id = (string)row.dev_id,
                    MacAddress = (string)row.dev_mac_address,
                    Name = (string)row.dev_name,
                    Category = (string)row.dev_category,
                    Status = (string)row.dev_status,
                    LimitMin = (decimal?)row.dev_limit_min,
                    LimitMax = (decimal?)row.dev_limit_max,
                    Photo = (string)row.dev_photo,
                    IsActive = (bool)row.dev_is_active,
                    LocationId = (int?)row.loc_id,
                    Gedung = (string)row.loc_gedung,
                    Lantai = (string)row.loc_lantai,
                    Ruangan = (string)row.loc_ruangan
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan detail device: {Id}", id);
                return null;
            }
        }

        public async Task<bool> ToggleDeviceStatusAsync(string id, string status, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    Id = id,
                    Status = status,
                    UpdatedBy = username
                };
                var rows = await conn.ExecuteScalarAsync<int>("IOT_SP_Device_SetStatus", parameters, commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status device: {Id} ke {Status} | User: {Username}", id, status, username);
                return false;
            }
        }

        public async Task<bool> ToggleDeviceActiveAsync(string id, bool isActive, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var rows = await conn.ExecuteScalarAsync<int>(
                    "IOT_SP_Device_ToggleActive",
                    new { DeviceId = id, IsActive = isActive },
                    commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status aktif device: {Id} menjadi {IsActive} | User: {Username}", id, isActive, username);
                return false;
            }
        }

        public async Task<bool> UpdateDeviceLocationAsync(string deviceId, int locationId, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    dev_id = deviceId,
                    loc_id = locationId,
                    UpdatedBy = username
                };
                var rows = await conn.ExecuteScalarAsync<int>("IOT_SP_Device_UpdateLocation", parameters, commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memindahkan device: {DeviceId} ke lokasi: {LocationId} | User: {Username}", deviceId, locationId, username);
                return false;
            }
        }

        public async Task<int> CreateLocationAsync(CreateLocationRequest dto, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    loc_gedung = dto.Gedung,
                    loc_lantai = dto.Lantai,
                    loc_ruangan = dto.Ruangan,
                    CreatedBy = username
                };
                var newId = await conn.ExecuteScalarAsync<int>("IOT_SP_Location_Create", parameters, commandType: CommandType.StoredProcedure);
                return newId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan lokasi baru: {Gedung} - {Ruangan} | User: {Username}", dto.Gedung, dto.Ruangan, username);
                return 0;
            }
        }

        public async Task<bool> UpdateLocationAsync(UpdateLocationRequest dto, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    loc_id = dto.Id,
                    loc_gedung = dto.Gedung,
                    loc_lantai = dto.Lantai,
                    loc_ruangan = dto.Ruangan,
                    UpdatedBy = username
                };
                var rows = await conn.ExecuteAsync("IOT_SP_Location_Update", parameters, commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengupdate lokasi: {Id} | User: {Username}", dto.Id, username);
                return false;
            }
        }

        public async Task<bool> ToggleLocationStatusAsync(int id, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    Id = id,
                    UpdatedBy = username
                };
                var rows = await conn.ExecuteAsync("IOT_SP_Location_SetStatus", parameters, commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal men-toggle status lokasi: {Id} | User: {Username}", id, username);
                return false;
            }
        }

        public async Task<IEnumerable<Location>> GetAllLocationsAsync(string? keyword, string? status)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    Keyword = keyword,
                    Status = status
                };
                var result = await conn.QueryAsync<dynamic>("IOT_SP_Location_GetData", parameters, commandType: CommandType.StoredProcedure);
                return result.Select(row => new Location
                {
                    Id = (int)row.loc_id,
                    Gedung = (string)row.loc_gedung,
                    Lantai = (string)row.loc_lantai,
                    Ruangan = (string)row.loc_ruangan,
                    Status = (string)row.loc_status,
                    ActiveDeviceCount = (int)row.ActiveDeviceCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data lokasi.");
                return [];
            }
        }


        public async Task<bool> InsertTelemetryAsync(InsertTelemetryRequest dto)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    dev_id = dto.DeviceId,
                    tel_value = dto.Value,
                    tel_type = dto.Type
                };
                var rows = await conn.ExecuteAsync("IOT_SP_Telemetry_Insert", parameters, commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menginjeksi telemetri: Device {DeviceId}, Value {Value}", dto.DeviceId, dto.Value);
                return false;
            }
        }

        public async Task<IEnumerable<Device>> GetDashboardLatestAsync(string? username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var result = await conn.QueryAsync<dynamic>("IOT_SP_Dashboard_GetLatest", new { Username = username }, commandType: CommandType.StoredProcedure);
                return result.Select(row => new Device
                {
                    Id = (string)row.dev_id,
                    Name = (string)row.dev_name,
                    Category = (string)row.dev_category,
                    Status = (string)row.dev_status,
                    IsActive = (bool)row.dev_is_active,
                    LimitMin = (decimal?)(row.LatestValue != null ? row.LatestValue : null),
                    LimitMax = (decimal?)(row.LatestHumidity != null ? row.LatestHumidity : null),
                    Photo = (string)row.ValueType,
                    Gedung = (string)row.loc_gedung,
                    Lantai = (string)row.loc_lantai,
                    Ruangan = (string)row.loc_ruangan
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data dashboard terbaru.");
                return [];
            }
        }

        public async Task<DashboardStats> GetDashboardStatsAsync(string? username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var result = await conn.QueryFirstOrDefaultAsync<DashboardStats>("IOT_SP_Dashboard_GetStats", new { Username = username }, commandType: CommandType.StoredProcedure);
                return result ?? new DashboardStats();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data statistik dashboard dari stored procedure.");
                return new DashboardStats();
            }
        }

        public async Task<IEnumerable<GraphPoint>> GetDashboardGraphAsync(string deviceId, string period, string? type = null)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var result = await conn.QueryAsync<dynamic>("IOT_SP_Dashboard_GetGraph", new { dev_id = deviceId, Period = period, tel_type = type }, commandType: CommandType.StoredProcedure);
                return result.Select(row => new GraphPoint
                {
                    AverageValue = (decimal)row.AverageValue,
                    MaximumValue = (decimal)row.MaximumValue,
                    MinimumValue = (decimal)row.MinimumValue,
                    LabelText = (string)row.LabelText
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data grafik telemetri untuk device: {DeviceId}", deviceId);
                return [];
            }
        }

        public async Task<IEnumerable<GraphPoint>> GetDashboardGraphAverageAsync(string category, string period)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var result = await conn.QueryAsync<dynamic>("IOT_SP_Dashboard_GetGraphAverage", new { Category = category, Period = period }, commandType: CommandType.StoredProcedure);
                return result.Select(row => new GraphPoint
                {
                    AverageValue = (decimal)row.AverageValue,
                    MaximumValue = (decimal)row.MaximumValue,
                    MinimumValue = (decimal)row.MinimumValue,
                    LabelText = (string)row.LabelText
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data grafik rata-rata telemetri untuk kategori: {Category}", category);
                return [];
            }
        }


        public async Task<bool> AssignAccessAsync(string targetUsername, string deviceId, string createdBy)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    Username = targetUsername,
                    dev_id = deviceId,
                    CreatedBy = createdBy
                };
                var rows = await conn.ExecuteAsync("IOT_SP_Access_Assign", parameters, commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menetapkan akses user: {Username} ke device: {DeviceId}", targetUsername, deviceId);
                return false;
            }
        }

        public async Task<bool> RemoveAccessAsync(string targetUsername, string deviceId)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    Username = targetUsername,
                    dev_id = deviceId
                };
                var rows = await conn.ExecuteAsync("IOT_SP_Access_Remove", parameters, commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus akses user: {Username} ke device: {DeviceId}", targetUsername, deviceId);
                return false;
            }
        }

        public async Task<bool> CheckAccessAsync(string username, string deviceId)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    Username = username,
                    dev_id = deviceId
                };
                var authorized = await conn.ExecuteScalarAsync<int>("IOT_SP_Access_Check", parameters, commandType: CommandType.StoredProcedure);
                return authorized > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengecek otorisasi user: {Username} ke device: {DeviceId}", username, deviceId);
                return false;
            }
        }


        public async Task<IEnumerable<Notification>> GetNotificationsAsync(string? username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var result = await conn.QueryAsync<dynamic>("IOT_SP_Notification_Get", new { Username = username }, commandType: CommandType.StoredProcedure);
                return result.Select(row => new Notification
                {
                    Id = (long)row.not_id,
                    DeviceId = (string)row.dev_id,
                    DeviceName = (string)row.dev_name,
                    Message = (string)row.not_message,
                    Status = (string)row.not_status,
                    TriggeredDate = (DateTime)row.not_triggered_date
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan daftar notifikasi.");
                return [];
            }
        }

        public async Task<bool> MarkNotificationAsReadAsync(long notId)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var affectedRows = await conn.ExecuteScalarAsync<int>("IOT_SP_Notification_Read", new { not_id = notId }, commandType: CommandType.StoredProcedure);
                return affectedRows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menandai notifikasi dibaca: {Id}", notId);
                return false;
            }
        }

        public async Task<bool> MarkNotificationsAsReadBulkAsync(List<long> ids)
        {
            try
            {
                if (ids == null || ids.Count == 0) return true;
                string idsStr = string.Join(",", ids);
                await using var conn = new SqlConnection(_conn);
                await conn.ExecuteAsync("dbo.IOT_SP_Notification_ReadBulk", new { Ids = idsStr }, commandType: CommandType.StoredProcedure);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menandai notifikasi (bulk) sebagai dibaca.");
                return false;
            }
        }

        public async Task<bool> DeleteNotificationsBulkAsync(List<long> ids)
        {
            try
            {
                if (ids == null || ids.Count == 0) return true;
                string idsStr = string.Join(",", ids);
                await using var conn = new SqlConnection(_conn);
                await conn.ExecuteAsync("dbo.IOT_SP_Notification_DeleteBulk", new { Ids = idsStr }, commandType: CommandType.StoredProcedure);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus notifikasi (bulk).");
                return false;
            }
        }

        public async Task<IEnumerable<GraphPoint>> GetDashboardGraphByDateAsync(string deviceId, DateTime selectedDate)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var result = await conn.QueryAsync<dynamic>("dbo.IOT_SP_Dashboard_GetGraphByDate", new { dev_id = deviceId, SelectedDate = selectedDate }, commandType: CommandType.StoredProcedure);
                return result.Select(row => new GraphPoint
                {
                    AverageValue = (decimal)(row.AverageValue != null ? row.AverageValue : 0m),
                    MaximumValue = (decimal)(row.MaximumValue != null ? row.MaximumValue : 0m),
                    MinimumValue = (decimal)(row.MinimumValue != null ? row.MinimumValue : 0m),
                    LabelText = (string)row.LabelText
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data grafik per tanggal: {DeviceId}", deviceId);
                return [];
            }
        }

        public async Task<bool> DeleteDeviceAsync(string id, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var rows = await conn.ExecuteScalarAsync<int>(
                    "IOT_SP_Device_Delete",
                    new { dev_id = id, DeletedBy = username },
                    commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus device: {Id} | User: {Username}", id, username);
                return false;
            }
        }

        // ── User Management ──────────────────────────────────────────────────

        public async Task<UserListResponse> GetAllUsersAsync(string? keyword, string? roleId, string? status)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new { Keyword = keyword, RoleId = roleId, Status = status };
                
                using var multi = await conn.QueryMultipleAsync("IOT_SP_User_GetAll", parameters, commandType: CommandType.StoredProcedure);
                
                var usersRaw = await multi.ReadAsync<dynamic>();
                var rolesRaw = await multi.ReadAsync<dynamic>();

                var users = usersRaw.Select(row => new AppUser
                {
                    Username  = (string)row.username,
                    FullName  = (string)row.fullName,
                    Email     = (string)(row.email ?? ""),
                    Status    = (string)row.status,
                    RoleName  = (string)row.roleName,
                    RoleId    = (string)row.roleId,
                    Photo     = (string?)row.photo
                }).ToList();

                var roles = rolesRaw.Select(row => new RoleCount
                {
                    RoleId   = (string)row.RoleId,
                    RoleName = (string)row.RoleName,
                    Count    = (int)row.Count
                }).ToList();

                return new UserListResponse
                {
                    Users = users,
                    Roles = roles
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan daftar pengguna.");
                return new UserListResponse();
            }
        }

        public async Task<AppUser?> GetUserDetailAsync(string username, string? viewerUsername = null)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                using var multi = await conn.QueryMultipleAsync(
                    "IOT_SP_User_GetDetail",
                    new { Username = username, ViewerUsername = viewerUsername },
                    commandType: CommandType.StoredProcedure);

                var userRow = await multi.ReadFirstOrDefaultAsync<dynamic>();
                if (userRow == null) return null;

                var devices = (await multi.ReadAsync<dynamic>()).Select(d => new DeviceAccess
                {
                    DeviceId   = (string)d.dev_id,
                    DeviceName = (string)d.dev_name,
                    Category   = (string)d.dev_category,
                    Status     = (string)d.dev_status
                }).ToList();

                return new AppUser
                {
                    Username  = (string)userRow.username,
                    FullName  = (string)userRow.fullName,
                    Email     = (string)(userRow.email ?? ""),
                    Status    = (string)userRow.status,
                    RoleName  = (string)userRow.roleName,
                    RoleId    = (string)userRow.roleId,
                    Photo     = (string?)userRow.photo,
                    CanManage = (bool)userRow.canManage,
                    AccessibleDevices = devices
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan detail pengguna: {Username}", username);
                return null;
            }
        }

        public async Task<bool> SetUserStatusAsync(string username, string status, string updatedBy)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var rows = await conn.ExecuteScalarAsync<int>(
                    "IOT_SP_User_SetStatus",
                    new { Username = username, Status = status, UpdatedBy = updatedBy },
                    commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status pengguna: {Username}", username);
                return false;
            }
        }

        public async Task<bool> CreateUserAsync(string username, string roleName, string status, string createdBy)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var rows = await conn.ExecuteScalarAsync<int>(
                    "IOT_SP_User_Create",
                    new { Username = username, RoleName = roleName, Status = status, CreatedBy = createdBy },
                    commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pengguna IoT baru: {Username}", username);
                return false;
            }
        }

        public async Task<bool> UpdateUserAsync(string username, string roleName, string status, string updatedBy)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var rows = await conn.ExecuteScalarAsync<int>(
                    "IOT_SP_User_Update",
                    new { Username = username, RoleName = roleName, Status = status, UpdatedBy = updatedBy },
                    commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengupdate pengguna IoT: {Username}", username);
                return false;
            }
        }

        public async Task<bool> DeleteUserAsync(string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var rows = await conn.ExecuteScalarAsync<int>(
                    "IOT_SP_User_Delete",
                    new { Username = username },
                    commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus pengguna IoT: {Username}", username);
                return false;
            }
        }

        public async Task<ReportSummary> GetReportSummaryAsync(string period)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                using var multi = await conn.QueryMultipleAsync(
                    "IOT_SP_Report_GetSummary",
                    new { Period = period },
                    commandType: CommandType.StoredProcedure);

                var device  = await multi.ReadFirstOrDefaultAsync<dynamic>();
                var temp    = await multi.ReadFirstOrDefaultAsync<dynamic>();
                var hum     = await multi.ReadFirstOrDefaultAsync<dynamic>();
                var power   = await multi.ReadFirstOrDefaultAsync<dynamic>();

                return new ReportSummary
                {
                    Period        = period,
                    TotalDevice   = device  != null ? (int)device.TotalDevice   : 0,
                    DeviceOnline  = device  != null ? (int)device.DeviceOnline  : 0,
                    DeviceOffline = device  != null ? (int)device.DeviceOffline : 0,
                    TotalUsers    = device  != null ? (int)(device.TotalUsers ?? 0) : 0,

                    AvgTemperature = temp   != null ? (decimal)temp.AvgTemperature : 0,
                    MaxTemperature = temp   != null ? (decimal)temp.MaxTemperature : 0,
                    MinTemperature = temp   != null ? (decimal)temp.MinTemperature : 0,
                    AvgHumidity    = hum    != null ? (decimal)hum.AvgHumidity    : 0,
                    MaxHumidity    = hum    != null ? (decimal)hum.MaxHumidity    : 0,
                    MinHumidity    = hum    != null ? (decimal)hum.MinHumidity    : 0,
                    TotalPowerKwh  = power  != null ? (decimal)power.TotalPowerKwh : 0,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil report summary, period: {Period}", period);
                return new ReportSummary { Period = period };
            }
        }

        // ── AC Settings ───────────────────────────────────────────────────────────

        public async Task<AcSettings?> GetAcSettingsAsync(string deviceId)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new DynamicParameters();
                parameters.Add("@dev_id", deviceId);
                var result = await conn.QueryFirstOrDefaultAsync<AcSettings>(
                    "IOT_SP_AC_Settings_Get", parameters, commandType: CommandType.StoredProcedure);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil AC settings untuk device: {DeviceId}", deviceId);
                return null;
            }
        }

        public async Task<bool> SaveAcSettingsAsync(string deviceId, AcSettings settings, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new DynamicParameters();
                parameters.Add("@dev_id",       deviceId);
                parameters.Add("@Temperature",  settings.Temperature);
                parameters.Add("@Mode",         settings.Mode);
                parameters.Add("@FanSpeed",     settings.FanSpeed);
                parameters.Add("@SwingMode",    settings.SwingMode);
                parameters.Add("@UpdatedBy",    username);
                var result = await conn.ExecuteScalarAsync<int>(
                    "IOT_SP_AC_Settings_Save", parameters, commandType: CommandType.StoredProcedure);
                return result == 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan AC settings untuk device: {DeviceId}", deviceId);
                return false;
            }
        }
    }
}
