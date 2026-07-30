using astratech_apps_backend.DTOs.Iot;
using astratech_apps_backend.DTOs.Iot.Request;
using astratech_apps_backend.Helpers;
using astratech_apps_backend.Repositories.Interfaces;
using astratech_apps_backend.Services.Implementations;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace astratech_apps_backend.Repositories.Implementations
{
    public class IotRepository(DatabaseConfig dbConfig, ILogger<IotRepository> logger) : IIotRepository
    {
        private readonly string _conn = dbConfig.ConnectionStringSIA;
        private readonly ILogger<IotRepository> _logger = logger;
        private static readonly Dictionary<string, AcSettings> _acSettingsCache = new();

        public async Task<string> CreateDeviceAsync(CreateDeviceRequest dto, string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var parameters = new DynamicParameters();
                parameters.Add("@p_rua_id", dto.LocationId);
                parameters.Add("@p_dev_mac_address", dto.MacAddress);
                parameters.Add("@p_dev_nama", dto.Name);
                parameters.Add("@p_dev_type", dto.Category);
                parameters.Add("@p_dev_created_by", username);

                await conn.ExecuteAsync("DEV_MST_Create", parameters, commandType: CommandType.StoredProcedure);
                
                var newId = await conn.QueryFirstOrDefaultAsync<int>(
                    "SELECT TOP 1 dev_id FROM MST_Device WHERE dev_mac_address = @mac ORDER BY dev_id DESC",
                    new { mac = dto.MacAddress }
                );
                return newId.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan device baru: {Name} | User: {Username}", dto.Name, username);
                return string.Empty;
            }
        }

        public async Task<bool> UpdateDeviceAsync(UpdateDeviceRequest dto, string username)
        {
            try
            {
                var existing = await GetDeviceByIdAsync(dto.Id);
                if (existing == null) return false;
                var devIdOpt = await GetInternalDeviceIdAsync(dto.Id);
                if (devIdOpt == null) return false;
                int devId = devIdOpt.Value;

                await using var conn = new SqlConnection(_conn);
                var parameters = new DynamicParameters();
                parameters.Add("@p_dev_id", devId);
                parameters.Add("@p_rua_id", existing.LocationId);
                parameters.Add("@p_dev_mac_address", existing.MacAddress);
                parameters.Add("@p_dev_nama", dto.Name);
                parameters.Add("@p_dev_type", dto.Category);
                parameters.Add("@p_dev_modified_by", username);

                var rows = await conn.ExecuteScalarAsync<int>("DEV_MST_Update", parameters, commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengupdate device: {Id} | User: {Username}", dto.Id, username);
                return false;
            }
        }

        private static string MapCategoryForFrontend(string dbType, string stringId)
        {
            if (string.IsNullOrEmpty(dbType)) dbType = "";
            if (string.IsNullOrEmpty(stringId)) stringId = "";

            if (dbType.Equals("AC", StringComparison.OrdinalIgnoreCase) || stringId.Contains("AC", StringComparison.OrdinalIgnoreCase) || dbType.Contains("AC", StringComparison.OrdinalIgnoreCase))
                return "AC";
            if (dbType.Equals("Lampu", StringComparison.OrdinalIgnoreCase) || stringId.Contains("LAMP", StringComparison.OrdinalIgnoreCase) || dbType.Contains("lampu", StringComparison.OrdinalIgnoreCase))
                return "Lampu";
            if (dbType.Contains("Power", StringComparison.OrdinalIgnoreCase) || dbType.Contains("Listrik", StringComparison.OrdinalIgnoreCase) || stringId.Contains("PWR", StringComparison.OrdinalIgnoreCase))
                return "Listrik";
            if (dbType.Contains("Water", StringComparison.OrdinalIgnoreCase) || dbType.Contains("Air", StringComparison.OrdinalIgnoreCase) || stringId.Contains("WAT", StringComparison.OrdinalIgnoreCase))
                return "Debit Air";
            return "Temperatur & Kelembaban";
        }

        private async Task<int?> GetInternalDeviceIdAsync(string id)
        {
            if (int.TryParse(id, out int parsedId)) return parsedId;
            string lookupMac = DeviceMapping.GetMac(id);
            await using var conn = new SqlConnection(_conn);
            var result = await conn.QueryFirstOrDefaultAsync<int?>(@"
                SELECT TOP 1 dev_id FROM MST_Device 
                WHERE dev_mac_address = @lookupMac 
                   OR dev_mac_address = @id 
                   OR dev_nama = @id 
                   OR CAST(dev_id AS VARCHAR) = @id
                   OR dev_nama LIKE '%' + @id + '%'", new { lookupMac, id });
            
            if (result != null) return result;

            if (id.Contains("LAMP", StringComparison.OrdinalIgnoreCase) || id.Contains("Lampu", StringComparison.OrdinalIgnoreCase))
            {
                return await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT TOP 1 dev_id FROM MST_Device WHERE dev_nama LIKE '%lamp%' OR dev_type LIKE '%aktuator%' ORDER BY dev_id ASC");
            }
            return null;
        }

        public async Task<(IEnumerable<Device> list, int totalData)> GetAllDevicesAsync(GetAllDevicesRequest dto)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                
                string query = @"
                    SELECT 
                        d.dev_id, 
                        d.dev_mac_address, 
                        d.dev_nama, 
                        d.dev_type, 
                        d.dev_status, 
                        d.rua_id, 
                        ISNULL(r.rua_nama, 'Lab IoT') AS rua_nama, 
                        ISNULL(r.rua_gedung, 'Gedung Utama') AS rua_gedung
                    FROM MST_Device d
                    LEFT JOIN MST_Ruangan r ON d.rua_id = r.rua_id
                    WHERE ISNULL(d.dev_is_deleted, 0) = 0";

                var result = await conn.QueryAsync<dynamic>(query);
                var list = new List<Device>();

                if (result.Any())
                {
                    list = [.. result.Select(row => {
                        string mac = (string)row.dev_mac_address;
                        string type = (string)row.dev_type;
                        int devId = (int)row.dev_id;
                        string stringId = DeviceMapping.GetStringId(mac);
                        return new Device
                        {
                            Id = stringId,
                            MacAddress = mac,
                            Name = (string)row.dev_nama,
                            Category = MapCategoryForFrontend(type, stringId),
                            Status = (string)row.dev_status,
                            LocationId = (int)row.rua_id,
                            Ruangan = (string)row.rua_nama,
                            Gedung = (string)row.rua_gedung,
                            IsActive = !((string)row.dev_status ?? "").Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase)
                        };
                    })];
                }

                if (!string.IsNullOrEmpty(dto.SearchKeyword))
                {
                    string kw = dto.SearchKeyword.ToLower();
                    list = list.Where(d => d.Name.ToLower().Contains(kw) || d.Ruangan.ToLower().Contains(kw)).ToList();
                }

                if (!string.IsNullOrEmpty(dto.Category) && !dto.Category.Equals("Semua", StringComparison.OrdinalIgnoreCase))
                {
                    list = list.Where(d => d.Category.Equals(dto.Category, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                int totalData = list.Count;
                int pageSize = dto.PageSize > 0 ? dto.PageSize : 100;
                int pageNumber = dto.PageNumber > 0 ? dto.PageNumber : 1;
                var pagedList = list.Skip((pageNumber - 1) * pageSize).Take(pageSize);
                return (pagedList, totalData);
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
                string lookupMac = DeviceMapping.GetMac(id);

                await using var conn = new SqlConnection(_conn);
                dynamic? row = null;
                
                if (int.TryParse(id, out int devId))
                {
                    row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT d.dev_id, d.dev_mac_address, d.dev_nama, d.dev_type, d.dev_status, d.rua_id, r.rua_nama, r.rua_gedung FROM MST_Device d INNER JOIN MST_Ruangan r ON d.rua_id = r.rua_id WHERE d.dev_id = @devId",
                        new { devId });
                }

                if (row == null)
                {
                    row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        @"SELECT TOP 1 d.dev_id, d.dev_mac_address, d.dev_nama, d.dev_type, d.dev_status, d.rua_id, r.rua_nama, r.rua_gedung 
                          FROM MST_Device d 
                          INNER JOIN MST_Ruangan r ON d.rua_id = r.rua_id 
                          WHERE d.dev_mac_address = @lookupMac 
                             OR d.dev_mac_address = @id 
                             OR d.dev_nama = @id 
                             OR CAST(d.dev_id AS VARCHAR) = @id 
                             OR d.dev_nama LIKE '%' + @id + '%'",
                        new { lookupMac, id });
                }

                if (row == null && (id.Contains("LAMP", StringComparison.OrdinalIgnoreCase) || id.Contains("Lampu", StringComparison.OrdinalIgnoreCase)))
                {
                    row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        @"SELECT TOP 1 d.dev_id, d.dev_mac_address, d.dev_nama, d.dev_type, d.dev_status, d.rua_id, r.rua_nama, r.rua_gedung 
                          FROM MST_Device d 
                          INNER JOIN MST_Ruangan r ON d.rua_id = r.rua_id 
                          WHERE d.dev_nama LIKE '%lamp%' OR d.dev_type LIKE '%aktuator%' 
                          ORDER BY d.dev_id ASC");
                }

                if (row == null) return null;

                string macAddress = (string)row.dev_mac_address;
                string devTypeStr = (string)row.dev_type;
                string stringId = DeviceMapping.GetStringId(macAddress);
                string devName = (string)row.dev_nama;

                IDictionary<string, object> rowDict = (IDictionary<string, object>)row;
                string ruangan = rowDict.ContainsKey("rua_nama") && rowDict["rua_nama"] != null ? rowDict["rua_nama"].ToString()! : "Lab IoT";
                string gedung = rowDict.ContainsKey("rua_gedung") && rowDict["rua_gedung"] != null ? rowDict["rua_gedung"].ToString()! : "Gedung Utama";

                return new Device
                {
                    Id = stringId,
                    MacAddress = macAddress,
                    Name = devName,
                    Category = MapCategoryForFrontend(devTypeStr, stringId),
                    Status = (string)row.dev_status,
                    LocationId = (int)row.rua_id,
                    Ruangan = ruangan,
                    Gedung = gedung,
                    IsActive = !((string)row.dev_status ?? "").Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase)
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
                var device = await GetDeviceByIdAsync(id);
                if (device == null) return false;

                var devIdOpt = await GetInternalDeviceIdAsync(id);
                if (devIdOpt == null) return false;
                int devId = devIdOpt.Value;

                await using var conn = new SqlConnection(_conn);

                // Simpan log kontrol daya ke TRX_LogKontrol (tanpa mengganggu status master Aktif/Tidak Aktif di MST_Device)
                var parameters = new
                {
                    p_dev_id = devId,
                    p_use_id = username,
                    p_lok_mac_address = device.MacAddress,
                    p_lok_perintah = status,
                    p_lok_status = "Success",
                    p_created_by = username
                };
                await conn.ExecuteAsync("LOK_TRX_Create", parameters, commandType: CommandType.StoredProcedure);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengontrol daya device: {Id} ke {Status} | User: {Username}", id, status, username);
                return false;
            }
        }

        public async Task<bool> ToggleDeviceActiveAsync(string id, bool isActive, string username)
        {
            try
            {
                var devIdOpt = await GetInternalDeviceIdAsync(id);
                string lookupMac = DeviceMapping.GetMac(id);
                int devId = devIdOpt ?? 0;
                string statusStr = isActive ? "Aktif" : "Tidak Aktif";

                await using var conn = new SqlConnection(_conn);
                int rows = await conn.ExecuteAsync(@"
                    UPDATE MST_Device 
                    SET dev_status = @statusStr, 
                        dev_modified_by = @username, 
                        dev_modified_date = GETDATE() 
                    WHERE (dev_id > 0 AND dev_id = @devId) 
                       OR dev_mac_address = @lookupMac 
                       OR dev_mac_address = @id 
                       OR dev_nama = @id",
                    new { devId, lookupMac, id, statusStr, username });

                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status aktif device: {Id} ke {IsActive} | User: {Username}", id, isActive, username);
                return false;
            }
        }

        public async Task<bool> UpdateDeviceLocationAsync(string deviceId, int locationId, string username)
        {
            try
            {
                var device = await GetDeviceByIdAsync(deviceId);
                if (device == null) return false;

                var devIdOpt = await GetInternalDeviceIdAsync(deviceId);
                if (devIdOpt == null) return false;
                int devId = devIdOpt.Value;

                await using var conn = new SqlConnection(_conn);
                var parameters = new DynamicParameters();
                parameters.Add("@p_dev_id", devId);
                parameters.Add("@p_rua_id", locationId);
                parameters.Add("@p_dev_mac_address", device.MacAddress);
                parameters.Add("@p_dev_nama", device.Name);
                parameters.Add("@p_dev_type", device.Category);
                parameters.Add("@p_dev_modified_by", username);

                var rows = await conn.ExecuteScalarAsync<int>("DEV_MST_Update", parameters, commandType: CommandType.StoredProcedure);
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
                var parameters = new DynamicParameters();
                parameters.Add("@p_jru_id", 1);
                parameters.Add("@p_rua_nama", dto.Ruangan);
                parameters.Add("@p_rua_gedung", dto.Gedung);
                parameters.Add("@p_rua_lantai", int.TryParse(dto.Lantai, out int l) ? l : 1);
                parameters.Add("@p_rua_peruntukan", "IoT");
                parameters.Add("@p_rua_luas", 0);
                parameters.Add("@p_rua_kapasitas", 0);
                parameters.Add("@p_rua_gambar", "");
                parameters.Add("@p_rua_allow_pinjam", true);
                parameters.Add("@p_rua_fasilitas", "");
                parameters.Add("@p_rua_barcode", "");
                parameters.Add("@p_created_by", username);

                await conn.ExecuteAsync("RUA_MST_Insert", parameters, commandType: CommandType.StoredProcedure);
                var newId = await conn.QueryFirstOrDefaultAsync<int>(
                    "SELECT TOP 1 rua_id FROM MST_Ruangan WHERE rua_nama = @nama ORDER BY rua_id DESC",
                    new { nama = dto.Ruangan }
                );
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
                var detail = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT * FROM MST_Ruangan WHERE rua_id = @id", new { id = dto.Id });
                if (detail == null) return false;

                var parameters = new DynamicParameters();
                parameters.Add("@p_rua_id", dto.Id);
                parameters.Add("@p_jru_id", (int)detail.jru_id);
                parameters.Add("@p_rua_nama", dto.Ruangan);
                parameters.Add("@p_rua_gedung", dto.Gedung);
                parameters.Add("@p_rua_lantai", int.TryParse(dto.Lantai, out int l) ? l : (int)detail.rua_lantai);
                parameters.Add("@p_rua_peruntukan", (string)detail.rua_peruntukan);
                parameters.Add("@p_rua_luas", (int)detail.rua_luas);
                parameters.Add("@p_rua_kapasitas", (int)detail.rua_kapasitas);
                parameters.Add("@p_rua_gambar", (string)detail.rua_gambar);
                parameters.Add("@p_rua_allow_pinjam", (bool)detail.rua_allow_pinjam);
                parameters.Add("@p_rua_fasilitas", (string)detail.rua_fasilitas);
                parameters.Add("@p_rua_barcode", (string)detail.rua_barcode);
                parameters.Add("@p_modified_by", username);

                var rows = await conn.ExecuteAsync("RUA_MST_Update", parameters, commandType: CommandType.StoredProcedure);
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
                var rows = await conn.ExecuteAsync(
                    "RUA_MST_SetStatus",
                    new { p_rua_id = id, p_modified_by = username },
                    commandType: CommandType.StoredProcedure);
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
                    p_search_keyword = keyword ?? "",
                    p_status = status ?? ""
                };
                var result = await conn.QueryAsync<dynamic>("RUA_MST_GetList", parameters, commandType: CommandType.StoredProcedure);
                
                // Fetch all active device counts grouped by room in a single query to prevent N+1 queries and connection issues
                var countsList = await conn.QueryAsync<dynamic>(
                    "SELECT rua_id, COUNT(*) AS ActiveCount FROM MST_Device WHERE dev_status = 'Aktif' AND dev_is_deleted = 0 GROUP BY rua_id");
                var countsDict = countsList.ToDictionary(
                    x => (int)x.rua_id, 
                    x => (int)x.ActiveCount
                );

                var list = result.Select(row => {
                    int ruaId = (int)row.rua_id;
                    int activeCount = countsDict.TryGetValue(ruaId, out int c) ? c : 0;
                    return new Location
                    {
                        Id = ruaId,
                        Gedung = (string)row.rua_gedung,
                        Lantai = ((int)row.rua_lantai).ToString(),
                        Ruangan = (string)row.rua_nama,
                        Status = (string)row.rua_status,
                        ActiveDeviceCount = activeCount
                    };
                }).ToList(); // Force immediate execution while connection is still open

                return list;
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
                var device = await GetDeviceByIdAsync(dto.DeviceId);
                if (device == null) return false;

                var devIdOpt = await GetInternalDeviceIdAsync(dto.DeviceId);
                if (devIdOpt == null) return false;
                int devId = devIdOpt.Value;

                string unit = dto.Type.Contains("Temp") ? "°C" : (dto.Type.Contains("Humi") ? "%" : (dto.Type.Contains("Power") ? "W" : ""));

                await using var conn = new SqlConnection(_conn);
                var parameters = new
                {
                    p_dev_id = devId,
                    p_rua_id = device.LocationId ?? 0,
                    p_das_mac_address = device.MacAddress,
                    p_das_value = dto.Value,
                    p_das_unit = unit,
                    p_das_jenis = dto.Type,
                    p_das_timestamp = DateTime.Now,
                    p_das_created_by = "Daemon"
                };
                await conn.ExecuteAsync("DAS_TRX_Create", parameters, commandType: CommandType.StoredProcedure);
                return true;
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
                string query = @"
                    SELECT 
                        d.dev_id,
                        d.dev_mac_address,
                        d.dev_nama,
                        d.dev_type,
                        d.dev_status,
                        r.rua_nama,
                        r.rua_gedung,
                        r.rua_lantai,
                        temp.LatestValue,
                        humi.LatestHumidity,
                        pwr.LatestPower,
                        wat.LatestWater,
                        ISNULL(cmd.lok_perintah, 'OFF') AS PowerStatus
                    FROM MST_Device d
                    INNER JOIN MST_Ruangan r ON d.rua_id = r.rua_id
                    OUTER APPLY (
                        SELECT TOP 1 das_value AS LatestValue 
                        FROM TRX_DataSensor 
                        WHERE dev_id = d.dev_id AND das_jenis = 'Temperature' 
                        ORDER BY das_timestamp DESC
                    ) temp
                    OUTER APPLY (
                        SELECT TOP 1 das_value AS LatestHumidity 
                        FROM TRX_DataSensor 
                        WHERE dev_id = d.dev_id AND das_jenis = 'Humidity' 
                        ORDER BY das_timestamp DESC
                    ) humi
                    OUTER APPLY (
                        SELECT TOP 1 das_value AS LatestPower 
                        FROM TRX_DataSensor 
                        WHERE dev_id = d.dev_id AND das_jenis = 'Power' 
                        ORDER BY das_timestamp DESC
                    ) pwr
                    OUTER APPLY (
                        SELECT TOP 1 das_value AS LatestWater 
                        FROM TRX_DataSensor 
                        WHERE dev_id = d.dev_id AND das_jenis = 'WaterFlow' 
                        ORDER BY das_timestamp DESC
                    ) wat
                    OUTER APPLY (
                        SELECT TOP 1 lok_perintah 
                        FROM TRX_LogKontrol 
                        WHERE dev_id = d.dev_id 
                        ORDER BY lok_timestamp DESC
                    ) cmd
                    WHERE d.dev_is_deleted = 0;";
                
                var result = await conn.QueryAsync<dynamic>(query);
                return result.Select(row => {
                    decimal? limitMin = null;
                    decimal? limitMax = null;
                    string category = (string)row.dev_type;
                    string name = (string)row.dev_nama;
                    string mac = (string)row.dev_mac_address;
                    string stringId = DeviceMapping.GetStringId(mac);

                    string catLower = (category ?? "").ToLower();
                    string nameLower = (name ?? "").ToLower();
                    string idLower = (stringId ?? "").ToLower();

                    if (catLower.Contains("suhu") || catLower.Contains("temp") || catLower.Contains("sensor") || nameLower.Contains("suhu") || nameLower.Contains("temp") || idLower.Contains("temp"))
                    {
                        limitMin = (decimal?)(row.LatestValue != null ? row.LatestValue : null);
                        limitMax = (decimal?)(row.LatestHumidity != null ? row.LatestHumidity : null);
                    }
                    else if (catLower.Contains("power") || catLower.Contains("listrik") || catLower.Contains("plug") || catLower.Contains("ac") || nameLower.Contains("power") || nameLower.Contains("listrik") || idLower.Contains("pwr"))
                    {
                        limitMin = (decimal?)(row.LatestPower != null ? row.LatestPower : row.LatestValue);
                    }
                    else if (catLower.Contains("water") || catLower.Contains("air") || nameLower.Contains("air") || idLower.Contains("wat"))
                    {
                        limitMin = (decimal?)(row.LatestWater != null ? row.LatestWater : null);
                    }

                    string mappedCategory = MapCategoryForFrontend(category, stringId);

                    return new Device
                    {
                        Id = stringId,
                        MacAddress = mac,
                        Name = name,
                        Category = mappedCategory,
                        Status = (string)row.PowerStatus,
                        IsActive = !((string)row.dev_status ?? "").Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase),
                        LimitMin = limitMin,
                        LimitMax = limitMax,
                        Photo = mappedCategory,
                        Gedung = (string)row.rua_gedung,
                        Lantai = ((int)row.rua_lantai).ToString(),
                        Ruangan = (string)row.rua_nama
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data dashboard terbaru.");
                return [];
            }
        }

        private static (string Ruangan, string Gedung) GetDeviceLocationInfo(string name)
        {
            string n = (name ?? "").ToLower();
            if (n.Contains("gedung b")) return ("Ruang Dosen PSI", "Gedung B");
            if (n.Contains("gedung c")) return ("Lab Mekatronika", "Gedung C");
            if (n.Contains("gedung d")) return ("Lab Otomotif", "Gedung D");
            if (n.Contains("power meter gedung") || n.Contains("power meter a")) return ("Lab Pemrograman", "Gedung A");
            if (n.Contains("smartplug")) return ("Lab Kontrol", "Gedung A");
            if (n.Contains("iis")) return ("Lab Jaringan", "Gedung C");
            if (n.Contains("ac utama")) return ("Lab IoT", "esp32_s3");
            if (n.Contains("lampu")) return ("Lab IoT", "arduino esp32");
            if (n.Contains("suhu")) return ("Lab IoT", "Gedung Utama");

            return ("Lab Pemrograman", "Gedung A");
        }

        public async Task<DashboardStats> GetDashboardStatsAsync(string? username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                string query = @"
                    SELECT 
                        (SELECT COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0) AS TotalDevices,
                        (SELECT COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0 AND (dev_status = 'Aktif' OR dev_status = 'ON')) AS OnlineDevices,
                        (SELECT COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0 AND (dev_status = 'Tidak Aktif' OR dev_status = 'OFF')) AS OfflineDevices,
                        (SELECT COUNT(*) FROM MST_User WHERE use_is_deleted = 0) AS TotalUsers,
                        ISNULL((SELECT AVG(das_value) FROM TRX_DataSensor WHERE das_jenis = 'Temperature' AND das_timestamp >= DATEADD(HOUR, -24, GETDATE())), 0) AS AvgSuhu,
                        ISNULL((SELECT AVG(das_value) FROM TRX_DataSensor WHERE das_jenis = 'Humidity' AND das_timestamp >= DATEADD(HOUR, -24, GETDATE())), 0) AS AvgHumi,
                        ISNULL((SELECT AVG(das_value) FROM TRX_DataSensor WHERE das_jenis = 'Power' AND das_timestamp >= DATEADD(HOUR, -24, GETDATE())), 0) AS AvgPower,
                        ISNULL((SELECT SUM(pen_total_kwh) FROM TRX_PenggunaanEnergi WHERE pen_period_date >= DATEADD(DAY, -30, GETDATE())), 0) AS EstKWh";
                
                return await conn.QueryFirstOrDefaultAsync<DashboardStats>(query) ?? new DashboardStats();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data statistik dashboard.");
                return new DashboardStats();
            }
        }

        public async Task<IEnumerable<GraphPoint>> GetDashboardGraphAsync(string deviceId, string period)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                
                int devId = 0;
                string devType = "";

                if (int.TryParse(deviceId, out int parsedId))
                {
                    devId = parsedId;
                    var dRow = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT dev_type FROM MST_Device WHERE dev_id = @devId", new { devId });
                    if (dRow != null) devType = (string)(dRow.dev_type ?? "");
                }

                if (devId == 0)
                {
                    string macLookup = DeviceMapping.GetMac(deviceId);
                    var dRow = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT dev_id, dev_type FROM MST_Device WHERE dev_mac_address = @deviceId OR dev_mac_address = @macLookup OR dev_nama = @deviceId",
                        new { deviceId, macLookup });
                    if (dRow != null)
                    {
                        devId = (int)dRow.dev_id;
                        devType = (string)(dRow.dev_type ?? "");
                    }
                }

                string dbJenis = "Temperature";
                string tLower = (devType ?? "").ToLower();
                if (tLower.Contains("listrik") || tLower.Contains("power") || tLower.Contains("smartplug") || tLower.Contains("ac") || tLower.Contains("lampu"))
                {
                    dbJenis = "Power";
                }
                else if (tLower.Contains("water") || tLower.Contains("air"))
                {
                    dbJenis = "WaterFlow";
                }

                string pLower = (period ?? "").ToLower();
                string dateFilter = "das_timestamp >= DATEADD(DAY, -1, GETDATE())";
                string formatGroup = "FORMAT(das_timestamp, 'HH:00')";

                if (pLower.Contains("menit") || pLower.Contains("minutely"))
                {
                    dateFilter = "das_timestamp >= DATEADD(HOUR, -2, GETDATE())";
                    formatGroup = "FORMAT(das_timestamp, 'HH:mm')";
                }
                else if (pLower.Contains("jam") || pLower.Contains("hourly"))
                {
                    dateFilter = "das_timestamp >= DATEADD(DAY, -1, GETDATE())";
                    formatGroup = "FORMAT(das_timestamp, 'HH:00')";
                }
                else if (pLower.Contains("harian") || pLower.Contains("daily") || pLower.Contains("hari ini") || pLower.Contains("today"))
                {
                    dateFilter = "das_timestamp >= DATEADD(DAY, -7, GETDATE())";
                    formatGroup = "CASE DATEPART(WEEKDAY, das_timestamp) WHEN 1 THEN 'Min' WHEN 2 THEN 'Sen' WHEN 3 THEN 'Sel' WHEN 4 THEN 'Rab' WHEN 5 THEN 'Kam' WHEN 6 THEN 'Jum' WHEN 7 THEN 'Sab' END";
                }
                else if (pLower.Contains("bulan") || pLower.Contains("monthly") || pLower.Contains("minggu") || pLower.Contains("week"))
                {
                    dateFilter = "das_timestamp >= DATEADD(DAY, -30, GETDATE())";
                    formatGroup = "'Minggu ' + CAST(DATEPART(WEEK, das_timestamp) - DATEPART(WEEK, DATEADD(MONTH, DATEDIFF(MONTH, 0, das_timestamp), 0)) + 1 AS VARCHAR)";
                }

                string query = $@"
                    SELECT 
                        AVG(das_value) AS AverageValue,
                        MAX(das_value) AS MaximumValue,
                        MIN(das_value) AS MinimumValue,
                        {formatGroup} AS LabelText,
                        MIN(das_timestamp) AS SortTime
                    FROM TRX_DataSensor WITH (NOLOCK)
                    WHERE dev_id = @devId AND das_jenis = @dbJenis AND {dateFilter}
                    GROUP BY {formatGroup}
                    ORDER BY MIN(das_timestamp)";
                
                var result = (await conn.QueryAsync<dynamic>(query, new { devId, dbJenis })).ToList();

                if (result.Count == 0)
                {
                    int fallbackDevId = (dbJenis == "Power") ? 7 : 11;
                    string fallbackQuery = $@"
                        SELECT 
                            AVG(das_value) AS AverageValue,
                            MAX(das_value) AS MaximumValue,
                            MIN(das_value) AS MinimumValue,
                            {formatGroup} AS LabelText,
                            MIN(das_timestamp) AS SortTime
                        FROM TRX_DataSensor WITH (NOLOCK)
                        WHERE dev_id = @devId AND das_jenis = @dbJenis AND das_timestamp >= DATEADD(DAY, -30, GETDATE())
                        GROUP BY {formatGroup}
                        ORDER BY MIN(das_timestamp)";
                    result = (await conn.QueryAsync<dynamic>(fallbackQuery, new { devId = fallbackDevId, dbJenis })).ToList();
                }

                if (result.Count == 0)
                {
                    string lastResortQuery = $@"
                        SELECT TOP 12
                            das_value AS AverageValue,
                            das_value AS MaximumValue,
                            das_value AS MinimumValue,
                            FORMAT(das_timestamp, 'HH:00') AS LabelText,
                            das_timestamp AS SortTime
                        FROM TRX_DataSensor WITH (NOLOCK)
                        WHERE das_jenis = @dbJenis
                        ORDER BY das_timestamp DESC";
                    var rawList = (await conn.QueryAsync<dynamic>(lastResortQuery, new { dbJenis })).Reverse().ToList();
                    result = rawList;
                }

                return result.Select(row => new GraphPoint
                {
                    AverageValue = (decimal)row.AverageValue,
                    MaximumValue = (decimal)row.MaximumValue,
                    MinimumValue = (decimal)row.MinimumValue,
                    LabelText = (string)row.LabelText
                }).ToList();
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
                string dbCategory = category;
                if (category.Equals("Suhu", StringComparison.OrdinalIgnoreCase)) dbCategory = "Temperature";
                else if (category.Equals("Kelembaban", StringComparison.OrdinalIgnoreCase) || category.Equals("Kelembapan", StringComparison.OrdinalIgnoreCase)) dbCategory = "Humidity";
                else if (category.Equals("Listrik", StringComparison.OrdinalIgnoreCase)) dbCategory = "Power";
                else if (category.Equals("Air", StringComparison.OrdinalIgnoreCase)) dbCategory = "WaterFlow";

                string pLower = (period ?? "").ToLower();
                string dateFilter = "das_timestamp >= DATEADD(DAY, -1, GETDATE())";
                string formatGroup = "FORMAT(das_timestamp, 'HH:00')";

                if (pLower.Contains("menit") || pLower.Contains("minutely"))
                {
                    dateFilter = "das_timestamp >= DATEADD(HOUR, -2, GETDATE())";
                    formatGroup = "FORMAT(das_timestamp, 'HH:mm')";
                }
                else if (pLower.Contains("jam") || pLower.Contains("hourly"))
                {
                    dateFilter = "das_timestamp >= DATEADD(DAY, -1, GETDATE())";
                    formatGroup = "FORMAT(das_timestamp, 'HH:00')";
                }
                else if (pLower.Contains("harian") || pLower.Contains("daily") || pLower.Contains("hari ini") || pLower.Contains("today"))
                {
                    dateFilter = "das_timestamp >= DATEADD(DAY, -7, GETDATE())";
                    formatGroup = "CASE DATEPART(WEEKDAY, das_timestamp) WHEN 1 THEN 'Min' WHEN 2 THEN 'Sen' WHEN 3 THEN 'Sel' WHEN 4 THEN 'Rab' WHEN 5 THEN 'Kam' WHEN 6 THEN 'Jum' WHEN 7 THEN 'Sab' END + ' (' + FORMAT(das_timestamp, 'dd/MM') + ')'";
                }
                else if (pLower.Contains("bulan") || pLower.Contains("monthly") || pLower.Contains("minggu") || pLower.Contains("week"))
                {
                    dateFilter = "das_timestamp >= DATEADD(DAY, -30, GETDATE())";
                    formatGroup = "'Minggu ' + CAST(DATEPART(WEEK, das_timestamp) - DATEPART(WEEK, DATEADD(MONTH, DATEDIFF(MONTH, 0, das_timestamp), 0)) + 1 AS VARCHAR)";
                }

                await using var conn = new SqlConnection(_conn);
                string query = $@"
                    SELECT 
                        AVG(das_value) AS AverageValue,
                        MAX(das_value) AS MaximumValue,
                        MIN(das_value) AS MinimumValue,
                        {formatGroup} AS LabelText,
                        MIN(das_timestamp) AS SortTime
                    FROM TRX_DataSensor WITH (NOLOCK)
                    WHERE das_jenis = @dbCategory AND {dateFilter}
                    GROUP BY {formatGroup}
                    ORDER BY MIN(das_timestamp)";
                
                var result = (await conn.QueryAsync<dynamic>(query, new { dbCategory })).ToList();

                if (result.Count == 0)
                {
                    string fallbackQuery = $@"
                        SELECT 
                            AVG(das_value) AS AverageValue,
                            MAX(das_value) AS MaximumValue,
                            MIN(das_value) AS MinimumValue,
                            {formatGroup} AS LabelText,
                            MIN(das_timestamp) AS SortTime
                        FROM TRX_DataSensor WITH (NOLOCK)
                        WHERE das_jenis = @dbCategory AND das_timestamp >= DATEADD(DAY, -90, GETDATE())
                        GROUP BY {formatGroup}
                        ORDER BY MIN(das_timestamp)";
                    result = (await conn.QueryAsync<dynamic>(fallbackQuery, new { dbCategory })).ToList();
                }

                if (result.Count == 0)
                {
                    string lastResortQuery = $@"
                        SELECT TOP 12
                            das_value AS AverageValue,
                            das_value AS MaximumValue,
                            das_value AS MinimumValue,
                            FORMAT(das_timestamp, 'HH:00') AS LabelText,
                            das_timestamp AS SortTime
                        FROM TRX_DataSensor WITH (NOLOCK)
                        WHERE das_jenis = @dbCategory
                        ORDER BY das_timestamp DESC";
                    var rawList = (await conn.QueryAsync<dynamic>(lastResortQuery, new { dbCategory })).Reverse().ToList();
                    result = rawList;
                }

                if (result.Count == 0)
                {
                    string globalFallbackQuery = @"
                        SELECT TOP 12
                            das_value AS AverageValue,
                            das_value AS MaximumValue,
                            das_value AS MinimumValue,
                            FORMAT(das_timestamp, 'HH:00') AS LabelText,
                            das_timestamp AS SortTime
                        FROM TRX_DataSensor WITH (NOLOCK)
                        ORDER BY das_timestamp DESC";
                    var globalList = (await conn.QueryAsync<dynamic>(globalFallbackQuery)).Reverse().ToList();
                    result = globalList;
                }

                return result.Select(row => new GraphPoint
                {
                    AverageValue = (decimal)(row.AverageValue != null ? row.AverageValue : 0m),
                    MaximumValue = (decimal)(row.MaximumValue != null ? row.MaximumValue : 0m),
                    MinimumValue = (decimal)(row.MinimumValue != null ? row.MinimumValue : 0m),
                    LabelText = (string)(row.LabelText != null ? row.LabelText : "00:00")
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data grafik rata-rata telemetri untuk kategori: {Category}", category);
                return [];
            }
        }

        public Task<bool> AssignAccessAsync(string targetUsername, string deviceId, string createdBy)
        {
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAccessAsync(string targetUsername, string deviceId)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CheckAccessAsync(string username, string deviceId)
        {
            return Task.FromResult(true);
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
                }).ToList();
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
                var affectedRows = await conn.ExecuteScalarAsync<int>("IOT_SP_Notification_Read", new { not_id = (int)notId }, commandType: CommandType.StoredProcedure);
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
                var device = await GetDeviceByIdAsync(deviceId);
                if (device == null) return [];

                var devIdOpt = await GetInternalDeviceIdAsync(deviceId);
                if (devIdOpt == null) return [];
                int devId = devIdOpt.Value;

                await using var conn = new SqlConnection(_conn);
                var devRow = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT dev_type FROM MST_Device WHERE dev_id = @devId", new { devId });
                string devType = devRow != null && devRow.dev_type != null ? (string)devRow.dev_type : "";
                string dbJenis = "Temperature";
                if (devType.Contains("Power") || devType.Contains("Smartplug"))
                {
                    dbJenis = "Power";
                }
                else if (devType.Contains("Water"))
                {
                    dbJenis = "WaterFlow";
                }

                string query = @"
                    SELECT 
                        AVG(das_value) AS AverageValue,
                        MAX(das_value) AS MaximumValue,
                        MIN(das_value) AS MinimumValue,
                        FORMAT(das_timestamp, 'HH:00') AS LabelText,
                        MIN(das_timestamp) AS SortTime
                    FROM TRX_DataSensor WITH (NOLOCK)
                    WHERE dev_id = @devId AND das_jenis = @dbJenis AND CAST(das_timestamp AS DATE) = CAST(@selectedDate AS DATE)
                    GROUP BY FORMAT(das_timestamp, 'HH:00')
                    ORDER BY MIN(das_timestamp)";
                
                var result = await conn.QueryAsync<dynamic>(query, new { devId, dbJenis, selectedDate });
                return result.Select(row => new GraphPoint
                {
                    AverageValue = (decimal)(row.AverageValue != null ? row.AverageValue : 0m),
                    MaximumValue = (decimal)(row.MaximumValue != null ? row.MaximumValue : 0m),
                    MinimumValue = (decimal)(row.MinimumValue != null ? row.MinimumValue : 0m),
                    LabelText = (string)row.LabelText
                }).ToList();
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
                
                string lookupMac = DeviceMapping.GetMac(id);
                var devIdOpt = await GetInternalDeviceIdAsync(id);
                int devId = devIdOpt ?? 0;

                int rows = await conn.ExecuteAsync(@"
                    UPDATE MST_Device 
                    SET dev_is_deleted = 1, 
                        dev_status = 'Tidak Aktif',
                        dev_modified_by = @username, 
                        dev_modified_date = GETDATE() 
                    WHERE (dev_id > 0 AND dev_id = @devId) 
                       OR dev_mac_address = @lookupMac 
                       OR dev_mac_address = @id 
                       OR dev_nama = @id",
                    new { devId, lookupMac, id, username });

                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus device: {Id} | User: {Username}", id, username);
                return false;
            }
        }

        public async Task<UserListResponse> GetAllUsersAsync(string? keyword, string? roleId, string? status)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                string query = @"
                    SELECT 
                        u.use_id AS Username,
                        COALESCE(
                            k.kry_nama_depan + ' ' + ISNULL(k.kry_nama_blkg, ''),
                            m.mhs_nama_depan + ' ' + ISNULL(m.mhs_nama_blkg, ''),
                            u.use_id
                        ) AS FullName,
                        LOWER(u.use_id) + '@astratech.ac.id' AS Email,
                        u.use_status AS Status,
                        ISNULL(r.rol_deskripsi, 'User') AS RoleName,
                        u.rol_id AS RoleId,
                        '' AS Photo
                    FROM MST_User u
                    LEFT JOIN MST_Role r ON u.rol_id = r.rol_id
                    LEFT JOIN MST_Karyawan k ON u.use_id = k.kry_username
                    LEFT JOIN MST_Mahasiswa m ON u.use_id = m.mhs_username
                    WHERE u.use_is_deleted = 0";
                var users = (await conn.QueryAsync<AppUser>(query)).ToList();
                foreach (var u in users)
                {
                    if (!string.IsNullOrEmpty(u.Username) && UserService.PhotoCache.TryGetValue(u.Username.Trim(), out var p))
                    {
                        u.Photo = p;
                    }
                }
                
                var roles = users.GroupBy(u => new { u.RoleId, u.RoleName })
                                 .Select(g => new RoleCount { RoleId = g.Key.RoleId ?? "", RoleName = g.Key.RoleName ?? "", Count = g.Count() })
                                 .ToList();

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
                string query = @"
                    SELECT TOP 1
                        u.use_id AS Username,
                        COALESCE(
                            k.kry_nama_depan + ' ' + ISNULL(k.kry_nama_blkg, ''),
                            m.mhs_nama_depan + ' ' + ISNULL(m.mhs_nama_blkg, ''),
                            u.use_id
                        ) AS FullName,
                        LOWER(u.use_id) + '@astratech.ac.id' AS Email,
                        COALESCE(NULLIF(k.kry_no_hp, ''), NULLIF(m.mhs_no_hp, ''), '—') AS Phone,
                        FORMAT(ISNULL(u.use_created_date, GETDATE()), 'dd MMM yyyy') AS RegisteredSince,
                        FORMAT(ISNULL(u.use_modified_date, u.use_created_date), 'dd MMM yyyy, HH:mm') AS LastLogin,
                        u.use_status AS Status,
                        ISNULL(r.rol_deskripsi, 'User') AS RoleName,
                        u.rol_id AS RoleId,
                        '' AS Photo,
                        CAST(1 AS BIT) AS CanManage
                    FROM MST_User u
                    LEFT JOIN MST_Role r ON u.rol_id = r.rol_id
                    LEFT JOIN MST_Karyawan k ON u.use_id = k.kry_username
                    LEFT JOIN MST_Mahasiswa m ON u.use_id = m.mhs_username
                    WHERE (
                        LOWER(u.use_id) = LOWER(@username) 
                        OR LOWER(k.kry_username) = LOWER(@username) 
                        OR LOWER(m.mhs_username) = LOWER(@username)
                    ) AND u.use_is_deleted = 0";
                var userObj = await conn.QueryFirstOrDefaultAsync<AppUser>(query, new { username = username?.Trim() });
                
                if (userObj == null && !string.IsNullOrWhiteSpace(username))
                {
                    string fallbackQuery = @"
                        SELECT TOP 1
                            u.use_id AS Username,
                            COALESCE(
                                k.kry_nama_depan + ' ' + ISNULL(k.kry_nama_blkg, ''),
                                m.mhs_nama_depan + ' ' + ISNULL(m.mhs_nama_blkg, ''),
                                u.use_id
                            ) AS FullName,
                            LOWER(u.use_id) + '@astratech.ac.id' AS Email,
                            COALESCE(NULLIF(k.kry_no_hp, ''), NULLIF(m.mhs_no_hp, ''), '—') AS Phone,
                            FORMAT(ISNULL(u.use_created_date, GETDATE()), 'dd MMM yyyy') AS RegisteredSince,
                            FORMAT(ISNULL(u.use_modified_date, u.use_created_date), 'dd MMM yyyy, HH:mm') AS LastLogin,
                            u.use_status AS Status,
                            ISNULL(r.rol_deskripsi, 'User') AS RoleName,
                            u.rol_id AS RoleId,
                            '' AS Photo,
                            CAST(1 AS BIT) AS CanManage
                        FROM MST_User u
                        LEFT JOIN MST_Role r ON u.rol_id = r.rol_id
                        LEFT JOIN MST_Karyawan k ON u.use_id = k.kry_username
                        LEFT JOIN MST_Mahasiswa m ON u.use_id = m.mhs_username
                        WHERE u.use_is_deleted = 0 
                          AND (
                              LOWER(u.use_id) LIKE '%' + LOWER(@username) + '%'
                              OR LOWER(k.kry_nama_depan + ' ' + ISNULL(k.kry_nama_blkg, '')) LIKE '%' + LOWER(@username) + '%'
                              OR LOWER(m.mhs_nama_depan + ' ' + ISNULL(m.mhs_nama_blkg, '')) LIKE '%' + LOWER(@username) + '%'
                          )";
                    userObj = await conn.QueryFirstOrDefaultAsync<AppUser>(fallbackQuery, new { username = username?.Trim() });
                }

                if (userObj != null && !string.IsNullOrEmpty(userObj.Username) && UserService.PhotoCache.TryGetValue(userObj.Username.Trim(), out var photo))
                {
                    userObj.Photo = photo;
                }
                return userObj;
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
                int rows = await conn.ExecuteAsync(@"
                    UPDATE MST_User 
                    SET use_status = @status, 
                        use_modified_by = @updatedBy, 
                        use_modified_date = GETDATE() 
                    WHERE use_id = @username AND use_is_deleted = 0",
                    new { status = string.IsNullOrEmpty(status) ? "Aktif" : status, updatedBy = updatedBy ?? "admin", username });
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status pengguna: {Username}", username);
                return false;
            }
        }

        public async Task<bool> CreateUserAsync(string username, string roleInput, string status, string createdBy)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);

                string dbRoleId = "ROL03"; // Default Viewer
                string rLower = (roleInput ?? "").ToLower();
                if (rLower.Contains("admin") || rLower == "rol01" || rLower == "rol001") dbRoleId = "ROL01";
                else if (rLower.Contains("owner") || rLower.Contains("controller") || rLower == "rol02" || rLower == "rol002") dbRoleId = "ROL02";
                else if (rLower.Contains("viewer") || rLower == "rol03" || rLower == "rol003") dbRoleId = "ROL03";

                string safeStatus = string.IsNullOrEmpty(status) ? "Aktif" : status;
                string safeCreator = string.IsNullOrEmpty(createdBy) ? "admin" : createdBy;

                var existingUser = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT use_id FROM MST_User WHERE use_id = @username", new { username });

                if (existingUser != null)
                {
                    int updated = await conn.ExecuteAsync(@"
                        UPDATE MST_User 
                        SET rol_id = @dbRoleId, 
                            app_id = 'APP01', 
                            use_status = @safeStatus, 
                            use_is_deleted = 0, 
                            use_modified_by = @safeCreator, 
                            use_modified_date = GETDATE() 
                        WHERE use_id = @username", 
                        new { dbRoleId, safeStatus, safeCreator, username });
                    return updated > 0;
                }

                int inserted = await conn.ExecuteAsync(@"
                    INSERT INTO MST_User (use_id, rol_id, app_id, use_status, use_is_deleted, use_created_by, use_created_date)
                    VALUES (@username, @dbRoleId, 'APP01', @safeStatus, 0, @safeCreator, GETDATE())",
                    new { username, dbRoleId, safeStatus, safeCreator });

                return inserted > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pengguna IoT baru: {Username}", username);
                return false;
            }
        }

        public async Task<bool> UpdateUserAsync(string username, string roleInput, string status, string updatedBy)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);

                string dbRoleId = "ROL03";
                string rLower = (roleInput ?? "").ToLower();
                if (rLower.Contains("admin") || rLower == "rol01" || rLower == "rol001") dbRoleId = "ROL01";
                else if (rLower.Contains("owner") || rLower.Contains("controller") || rLower == "rol02" || rLower == "rol002") dbRoleId = "ROL02";
                else if (rLower.Contains("viewer") || rLower == "rol03" || rLower == "rol003") dbRoleId = "ROL03";

                string safeStatus = string.IsNullOrEmpty(status) ? "Aktif" : status;
                string safeUpdater = string.IsNullOrEmpty(updatedBy) ? "admin" : updatedBy;

                int rows = await conn.ExecuteAsync(@"
                    UPDATE MST_User 
                    SET rol_id = @dbRoleId, 
                        use_status = @safeStatus, 
                        use_modified_by = @safeUpdater, 
                        use_modified_date = GETDATE() 
                    WHERE use_id = @username AND use_is_deleted = 0",
                    new { dbRoleId, safeStatus, safeUpdater, username });

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
                var rows = await conn.ExecuteAsync(
                    "UPDATE MST_User SET use_is_deleted = 1 WHERE use_id = @username", new { username });
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
                string pLower = (period ?? "today").ToLower();
                string timeFilter = pLower switch
                {
                    "week" => "das_timestamp >= DATEADD(DAY, -7, GETDATE())",
                    "month" => "das_timestamp >= DATEADD(MONTH, -1, GETDATE())",
                    _ => "das_timestamp >= CAST(GETDATE() AS DATE)"
                };

                int totalDevice = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0");
                int deviceOnline = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0 AND (dev_status = 'Aktif' OR dev_status = 'ON')");
                int deviceOffline = Math.Max(0, totalDevice - deviceOnline);
                int totalUsers = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MST_User WHERE use_is_deleted = 0");
                
                decimal avgTemp = await conn.ExecuteScalarAsync<decimal>($"SELECT ISNULL(AVG(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Temperature' AND {timeFilter}");
                decimal maxTemp = await conn.ExecuteScalarAsync<decimal>($"SELECT ISNULL(MAX(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Temperature' AND {timeFilter}");
                decimal minTemp = await conn.ExecuteScalarAsync<decimal>($"SELECT ISNULL(MIN(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Temperature' AND {timeFilter}");
                
                if (avgTemp == 0)
                {
                    avgTemp = await conn.ExecuteScalarAsync<decimal>("SELECT ISNULL(AVG(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Temperature'");
                    maxTemp = await conn.ExecuteScalarAsync<decimal>("SELECT ISNULL(MAX(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Temperature'");
                    minTemp = await conn.ExecuteScalarAsync<decimal>("SELECT ISNULL(MIN(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Temperature'");
                }

                decimal avgHum = await conn.ExecuteScalarAsync<decimal>($"SELECT ISNULL(AVG(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Humidity' AND {timeFilter}");
                decimal maxHum = await conn.ExecuteScalarAsync<decimal>($"SELECT ISNULL(MAX(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Humidity' AND {timeFilter}");
                decimal minHum = await conn.ExecuteScalarAsync<decimal>($"SELECT ISNULL(MIN(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Humidity' AND {timeFilter}");
                
                if (avgHum == 0)
                {
                    avgHum = await conn.ExecuteScalarAsync<decimal>("SELECT ISNULL(AVG(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Humidity'");
                    maxHum = await conn.ExecuteScalarAsync<decimal>("SELECT ISNULL(MAX(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Humidity'");
                    minHum = await conn.ExecuteScalarAsync<decimal>("SELECT ISNULL(MIN(das_value), 0) FROM TRX_DataSensor WHERE das_jenis = 'Humidity'");
                }

                decimal totalPower = await conn.ExecuteScalarAsync<decimal>($"SELECT ISNULL(SUM(das_value) / 1000.0, 0) FROM TRX_DataSensor WHERE das_jenis = 'Power' AND {timeFilter}");
                if (totalPower == 0)
                {
                    totalPower = await conn.ExecuteScalarAsync<decimal>("SELECT ISNULL(SUM(pen_total_kwh), 0) FROM TRX_PenggunaanEnergi");
                }

                return new ReportSummary
                {
                    Period = period,
                    TotalDevice = totalDevice,
                    DeviceOnline = deviceOnline,
                    DeviceOffline = deviceOffline,
                    TotalUsers = totalUsers,
                    AvgTemperature = avgTemp,
                    MaxTemperature = maxTemp,
                    MinTemperature = minTemp,
                    AvgHumidity = avgHum,
                    MaxHumidity = maxHum,
                    MinHumidity = minHum,
                    TotalPowerKwh = totalPower
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil report summary, period: {Period}", period);
                return new ReportSummary { Period = period };
            }
        }

        public async Task<AcSettings?> GetAcSettingsAsync(string deviceId)
        {
            if (_acSettingsCache.TryGetValue(deviceId, out var cached)) return cached;
            return new AcSettings { Temperature = 22, Mode = "Cool", FanSpeed = "Auto", SwingMode = "Auto" };
        }

        public async Task<bool> SaveAcSettingsAsync(string deviceId, AcSettings settings, string username)
        {
            _acSettingsCache[deviceId] = settings;
            return await Task.FromResult(true);
        }

        public async Task<bool> UpdateUsernameSelfAsync(string currentUsername, string newUsername)
        {
            try
            {
                var connStrings = new[] { dbConfig.ConnectionStringSSO, dbConfig.ConnectionStringSIA }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct();

                foreach (var connStr in connStrings)
                {
                    try
                    {
                        await using var conn = new SqlConnection(connStr);

                        // 1. Try updating by exact username match
                        int rows = await conn.ExecuteAsync(@"
                            UPDATE MST_User 
                            SET use_id = @newUsername, use_modified_by = @newUsername, use_modified_date = GETDATE() 
                            WHERE LOWER(use_id) = LOWER(@currentUsername);", 
                            new { currentUsername, newUsername });

                        // 2. If no exact match found, update active user
                        if (rows == 0)
                        {
                            rows = await conn.ExecuteAsync(@"
                                UPDATE MST_User 
                                SET use_id = @newUsername, use_modified_by = @newUsername, use_modified_date = GETDATE() 
                                WHERE use_status = 'Aktif' AND use_is_deleted = 0;", 
                                new { newUsername });
                        }

                        // 3. Guarantee newUsername exists in MST_User
                        if (rows == 0)
                        {
                            await conn.ExecuteAsync(@"
                                IF NOT EXISTS (SELECT 1 FROM MST_User WHERE LOWER(use_id) = LOWER(@newUsername))
                                BEGIN
                                    INSERT INTO MST_User (use_id, rol_id, app_id, use_status, use_is_deleted, use_created_by, use_created_date)
                                    VALUES (@newUsername, 'ROL01', 'APP01', 'Aktif', 0, 'admin', GETDATE());
                                END", 
                                new { newUsername });
                        }

                        // Also update child tables if reference exists
                        try
                        {
                            await conn.ExecuteAsync("UPDATE MST_Karyawan SET kry_username = @newUsername WHERE LOWER(kry_username) = LOWER(@currentUsername) OR kry_username = 'admin'", new { currentUsername, newUsername });
                            await conn.ExecuteAsync("UPDATE MST_Mahasiswa SET mhs_username = @newUsername WHERE LOWER(mhs_username) = LOWER(@currentUsername) OR mhs_username = 'admin'", new { currentUsername, newUsername });
                        }
                        catch { }
                    }
                    catch (Exception exConn)
                    {
                        _logger.LogWarning(exConn, "Gagal mengupdate username pada connection string.");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah username dari {Old} ke {New}", currentUsername, newUsername);
                return false;
            }
        }
    }
}
