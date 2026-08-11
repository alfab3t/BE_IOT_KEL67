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
        private static readonly Dictionary<string, AcSettings> _acSettingsCache = [];

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
                    "DEV_MST_GetIdByMac",
                    new { mac = dto.MacAddress },
                    commandType: CommandType.StoredProcedure
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
                await using var conn = new SqlConnection(_conn);

                string lookupMac = DeviceMapping.GetMac(dto.Id);

                var devRow = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "DEV_MST_GetByMacOrName",
                    new { id = dto.Id, lookupMac },
                    commandType: CommandType.StoredProcedure
                );

                int devId = 0;
                string mac = lookupMac;
                int ruaId = 1;

                if (devRow != null)
                {
                    var row = (IDictionary<string, object>)devRow;

                    if (row.TryGetValue("dev_id", out var devIdValue) && devIdValue != null)
                    {
                        devId = Convert.ToInt32(devIdValue);
                    }

                    if (row.TryGetValue("dev_mac_address", out var macValue) && macValue != null)
                    {
                        mac = Convert.ToString(macValue) ?? lookupMac;
                    }

                    if (row.TryGetValue("rua_id", out var ruaIdValue) && ruaIdValue != null)
                    {
                        ruaId = Convert.ToInt32(ruaIdValue);
                    }
                }

                if (devId == 0 && int.TryParse(dto.Id, out int pId))
                    devId = pId;

                var parameters = new DynamicParameters();
                parameters.Add("@p_dev_id", devId);
                parameters.Add("@p_rua_id", ruaId);
                parameters.Add("@p_dev_mac_address", mac);
                parameters.Add("@p_dev_nama", dto.Name);
                parameters.Add("@p_dev_type", dto.Category);
                parameters.Add("@p_dev_modified_by", username);

                await conn.ExecuteAsync(
                    "DEV_MST_Update",
                    parameters,
                    commandType: CommandType.StoredProcedure);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Gagal mengupdate device: {Id} | User: {Username}",
                    dto.Id,
                    username);

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
            var result = await conn.QueryFirstOrDefaultAsync<int?>(
                "DEV_MST_GetInternalId",
                new { lookupMac, id },
                commandType: CommandType.StoredProcedure
            );
            
            return result;
        }

        public async Task<(IEnumerable<Device> list, int totalData)> GetAllDevicesAsync(GetAllDevicesRequest dto)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var result = await conn.QueryAsync<dynamic>(
                    "DEV_MST_GetAll",
                    commandType: CommandType.StoredProcedure
                );

                var list = new List<Device>();

                if (result != null && result.Any())
                {
                    list = [.. result.Select(row => {
                        string mac = row.dev_mac_address != null ? Convert.ToString(row.dev_mac_address)! : "";
                        string type = row.dev_type != null ? Convert.ToString(row.dev_type)! : "";
                        int devId = row.dev_id != null ? Convert.ToInt32(row.dev_id) : 0;
                        string stringId = DeviceMapping.GetStringId(mac);
                        string status = row.dev_status != null ? Convert.ToString(row.dev_status)! : "OFF";
                        return new Device
                        {
                            Id = stringId,
                            MacAddress = mac,
                            Name = row.dev_nama != null ? Convert.ToString(row.dev_nama)! : "",
                            Category = MapCategoryForFrontend(type, stringId),
                            Status = status,
                            LocationId = row.rua_id != null ? Convert.ToInt32(row.rua_id) : 1,
                            Ruangan = row.rua_nama != null ? Convert.ToString(row.rua_nama)! : "Lab IoT",
                            Gedung = row.rua_gedung != null ? Convert.ToString(row.rua_gedung)! : "Gedung Utama",
                            IsActive = !status.Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase)
                        };
                    })];
                }

                if (!string.IsNullOrEmpty(dto.SearchKeyword))
                {
                    string kw = dto.SearchKeyword.ToLower();
                    list = [.. list.Where(d =>
                        (d.Name ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        (d.Ruangan ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase))];
                }

                if (!string.IsNullOrEmpty(dto.Category) && !dto.Category.Equals("Semua", StringComparison.OrdinalIgnoreCase))
                {
                    list = [..list.Where(d => d.Category.Equals(dto.Category, StringComparison.OrdinalIgnoreCase)).ToList()];
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

                var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "DEV_MST_GetById",
                    new { id, lookupMac },
                    commandType: CommandType.StoredProcedure
                );

                if (row == null)
                {
                    return new Device
                    {
                        Id = id,
                        MacAddress = lookupMac,
                        Name = id,
                        Category = MapCategoryForFrontend("Sensor", id),
                        Status = "OFF",
                        LocationId = 1,
                        Ruangan = "Lab IoT",
                        Gedung = "Gedung Utama",
                        IsActive = true,
                        LocationHistory = []
                    };
                }

                IDictionary<string, object> rowDict = (IDictionary<string, object>)row;

                string macAddress = lookupMac;
                if (rowDict.TryGetValue("dev_mac_address", out var macVal) && macVal != null) macAddress = macVal.ToString()!;

                string devTypeStr = "Sensor";
                if (rowDict.TryGetValue("dev_type", out var typeVal) && typeVal != null) devTypeStr = typeVal.ToString()!;

                string stringId = DeviceMapping.GetStringId(macAddress);
                if (string.IsNullOrEmpty(stringId) || stringId == macAddress) stringId = id;

                string devName = id;
                if (rowDict.TryGetValue("dev_nama", out var nameVal) && nameVal != null) devName = nameVal.ToString()!;

                int devIdInt = 0;
                if (rowDict.TryGetValue("dev_id", out var idVal) && idVal != null) devIdInt = Convert.ToInt32(idVal);

                int locationIdInt = 1;
                if (rowDict.TryGetValue("rua_id", out var locVal) && locVal != null) locationIdInt = Convert.ToInt32(locVal);

                string ruangan = "Lab IoT";
                if (rowDict.TryGetValue("rua_nama", out var ruaVal) && ruaVal != null) ruangan = ruaVal.ToString()!;

                string gedung = "Gedung Utama";
                if (rowDict.TryGetValue("rua_gedung", out var gdgVal) && gdgVal != null) gedung = gdgVal.ToString()!;

                string devStatus = "OFF";
                if (rowDict.TryGetValue("dev_status", out var stVal) && stVal != null) devStatus = stVal.ToString()!;

                string mappedCat = MapCategoryForFrontend(devTypeStr, stringId);
                bool includeLocationHistory = !mappedCat.Equals("Listrik", StringComparison.OrdinalIgnoreCase);

                var realHistory = new List<LocationHistoryItem>();
                if (includeLocationHistory)
                {
                    try
                    {
                        var logs = await conn.QueryAsync<dynamic>(
                            "LOK_TRX_GetByDevId",
                            new { devIdInt, macAddress, id, stringId, lookupMac },
                            commandType: CommandType.StoredProcedure
                        );

                        var locLogs = logs != null ? [.. logs.Where(l => {
                            IDictionary<string, object> dict = (IDictionary<string, object>)l;
                            string status = dict.TryGetValue("lok_status", out var statusValue) && statusValue != null? statusValue.ToString()!: "";
                            string cmdText = dict.TryGetValue("lok_perintah", out var cmdValue) && cmdValue != null? cmdValue.ToString()!: "";
                            bool isStatusOrPowerCmd = 
                                cmdText.Equals("ON", StringComparison.OrdinalIgnoreCase) || 
                                cmdText.Equals("OFF", StringComparison.OrdinalIgnoreCase) || 
                                cmdText.Equals("Aktif", StringComparison.OrdinalIgnoreCase) || 
                                cmdText.Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase) || 
                                cmdText.Equals("Success", StringComparison.OrdinalIgnoreCase) || 
                                cmdText.Equals("0") || cmdText.Equals("1");

                            if (isStatusOrPowerCmd) return false;

                            return status.Equals("LocationChange", StringComparison.OrdinalIgnoreCase) ||
                                   cmdText.StartsWith("Dipindahkan", StringComparison.OrdinalIgnoreCase) ||
                                   cmdText.Contains("Lab", StringComparison.OrdinalIgnoreCase) ||
                                   cmdText.Contains("Ruang", StringComparison.OrdinalIgnoreCase) ||
                                   cmdText.Contains("Gedung", StringComparison.OrdinalIgnoreCase) ||
                                   cmdText.Contains("cb", StringComparison.OrdinalIgnoreCase) ||
                                   cmdText.Contains('(');
                        })] : new List<dynamic>();

                        if (locLogs.Count > 0)
                        {
                            static string cleanRoom(string rawStr, string defaultRoom)
                            {
                                if (string.IsNullOrWhiteSpace(rawStr))
                                    return defaultRoom;

                                string s = rawStr.Trim();

                                if (s.StartsWith("Dipindahkan ke ", StringComparison.OrdinalIgnoreCase))
                                    s = s["Dipindahkan ke ".Length..];
                                else if (s.StartsWith("Dipindahkan ", StringComparison.OrdinalIgnoreCase))
                                    s = s["Dipindahkan ".Length..];
                                else if (s.Equals("Dipindahkan", StringComparison.OrdinalIgnoreCase) ||
                                         s.Equals("Dipindahka", StringComparison.OrdinalIgnoreCase))
                                    return defaultRoom;

                                return string.IsNullOrWhiteSpace(s) ? defaultRoom : s;
                            }

                            var rawParsed = locLogs.Select(l =>
                            {
                                IDictionary<string, object> lDict = (IDictionary<string, object>)l;

                                object? tsObj = lDict.TryGetValue("lok_timestamp", out var timestampValue) && timestampValue != null
                                    ? timestampValue
                                    : null;

                                DateTime dt = tsObj != null
                                    ? Convert.ToDateTime(tsObj)
                                    : DateTime.Now;

                                string rawCmd = lDict.TryGetValue("lok_perintah", out var commandValue) && commandValue != null
                                    ? commandValue.ToString()!
                                    : "";

                                string by = lDict.TryGetValue("use_id", out var userValue) && userValue != null
                                    ? userValue.ToString()!
                                    : lDict.TryGetValue("lok_created_by", out var createdByValue) && createdByValue != null
                                        ? createdByValue.ToString()!
                                        : "Admin";

                                string roomName = cleanRoom(rawCmd, ruangan);

                                return new { dt, rawCmd, roomName, by };
                            }).OrderBy(x => x.dt).ToList();

                            var roomGroups = new List<(string RoomName, DateTime StartDt, DateTime EndDt, string UpdatedBy)>();
                            if (rawParsed.Count > 0)
                            {
                                string curRoom = rawParsed[0].roomName;
                                DateTime sDt = rawParsed[0].dt;
                                DateTime eDt = rawParsed[0].dt;
                                string curBy = rawParsed[0].by;

                                for (int i = 1; i < rawParsed.Count; i++)
                                {
                                    var item = rawParsed[i];
                                    if (string.Equals(item.roomName, curRoom, StringComparison.OrdinalIgnoreCase))
                                    {
                                        eDt = item.dt;
                                        if (!string.IsNullOrEmpty(item.by)) curBy = item.by;
                                    }
                                    else
                                    {
                                        roomGroups.Add((curRoom, sDt, item.dt, curBy));
                                        curRoom = item.roomName;
                                        sDt = item.dt;
                                        eDt = item.dt;
                                        curBy = item.by;
                                    }
                                }
                                roomGroups.Add((curRoom, sDt, eDt, curBy));
                            }

                            #pragma warning disable IDE0042
                            for (int i = 0; i < roomGroups.Count; i++)
                            {
                                var grp = roomGroups[i];
                                string periodText;

                                if (i < roomGroups.Count - 1)
                                {
                                    var nextGrp = roomGroups[i + 1];
                                    DateTime startDt = grp.StartDt;
                                    DateTime endDt = nextGrp.StartDt;

                                    if (startDt.Date == endDt.Date)
                                    {
                                        periodText = $"{startDt:d MMM yyyy, HH:mm} - {endDt:HH:mm}";
                                    }
                                    else if (startDt.Month == endDt.Month && startDt.Year == endDt.Year)
                                    {
                                        int startDay = startDt.Day;
                                        int endDay = endDt.Date > startDt.Date ? endDt.AddDays(-1).Day : startDt.Day;
                                        if (endDay < startDay) endDay = startDay;
                                        if (startDay == endDay)
                                            periodText = $"{startDay} {startDt:MMM yyyy}";
                                        else
                                            periodText = $"{startDay}-{endDay} {startDt:MMM yyyy}";
                                    }
                                    else
                                    {
                                        DateTime endDate = endDt.AddDays(-1);
                                        if (endDate < startDt) endDate = startDt;
                                        periodText = $"{startDt:d MMM yyyy} - {endDate:d MMM yyyy}";
                                    }
                                }
                                else
                                {
                                    periodText = $"{grp.StartDt:d MMM yyyy} - Sekarang";
                                }

                                string locationDisplay;
                                if (i > 0)
                                {
                                    string prevRoom = roomGroups[i - 1].RoomName;
                                    string currRoom = grp.RoomName;
                                    locationDisplay = $"Dari {prevRoom} ke {currRoom}";
                                }
                                else
                                {
                                    locationDisplay = grp.RoomName;
                                }

                                realHistory.Add(new LocationHistoryItem
                                {
                                    PeriodText = periodText,
                                    Ruangan = locationDisplay,
                                    Gedung = gedung,
                                    UpdatedBy = grp.UpdatedBy,
                                    TimestampText = grp.StartDt.ToString("dd MMM yyyy")
                                });
                            }

                            realHistory.Reverse();
                            #pragma warning restore IDE0042
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal memproses history lokasi device: {Id}", id);
                    }
                }

                return new Device
                {
                    Id = stringId,
                    MacAddress = macAddress,
                    Name = devName,
                    Category = mappedCat,
                    Status = devStatus,
                    LocationId = locationIdInt,
                    Ruangan = ruangan,
                    Gedung = gedung,
                    IsActive = !devStatus.Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase),
                    LocationHistory = realHistory
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
                string lookupMac = DeviceMapping.GetMac(id);
                var devIdOpt = await GetInternalDeviceIdAsync(id);
                int devId = devIdOpt ?? 0;

                await using var conn = new SqlConnection(_conn);

                await conn.ExecuteAsync(
                    "DEV_MST_SetStatus",
                    new { devId, lookupMac, id, statusStr = status, username },
                    commandType: CommandType.StoredProcedure
                );

                var parameters = new
                {
                    p_dev_id = devId,
                    p_use_id = username,
                    p_lok_mac_address = lookupMac,
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
                await conn.ExecuteAsync(
                    "DEV_MST_SetStatus",
                    new { devId, lookupMac, id, statusStr, username },
                    commandType: CommandType.StoredProcedure
                );

                return true;
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
                await using var conn = new SqlConnection(_conn);
                string lookupMac = DeviceMapping.GetMac(deviceId);

                var devRow = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "DEV_MST_GetById",
                    new { id = deviceId, lookupMac },
                    commandType: CommandType.StoredProcedure
                );

                int devId = 0;
                string mac = lookupMac;

                if (devRow != null)
                {
                    IDictionary<string, object> dDict = (IDictionary<string, object>)devRow;
                    if (dDict.TryGetValue("dev_id", out var idVal) && idVal != null) devId = Convert.ToInt32(idVal);
                    if (dDict.TryGetValue("dev_mac_address", out var macVal) && macVal != null) mac = macVal.ToString()!;
                }
                if (devId == 0 && int.TryParse(deviceId, out int pId)) devId = pId;

                var updateParams = new DynamicParameters();
                updateParams.Add("@p_dev_id", devId);
                updateParams.Add("@p_rua_id", locationId);
                updateParams.Add("@p_dev_mac_address", mac);
                updateParams.Add("@p_dev_nama", (string?)null);
                updateParams.Add("@p_dev_type", (string?)null);
                updateParams.Add("@p_dev_modified_by", username);

                await conn.ExecuteAsync("DEV_MST_Update", updateParams, commandType: CommandType.StoredProcedure);

                string targetNamaRuangan = "Ruangan";
                string targetNamaGedung = "Gedung Utama";
                try
                {
                    var rRow = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "RUA_MST_GetDetail", new { id = locationId }, commandType: CommandType.StoredProcedure);
                    if (rRow != null)
                    {
                        IDictionary<string, object> rDict = (IDictionary<string, object>)rRow;
                        if (rDict.TryGetValue("Name", out var rn) && rn != null) targetNamaRuangan = rn.ToString()!;
                        else if (rDict.TryGetValue("rua_nama", out var rn2) && rn2 != null) targetNamaRuangan = rn2.ToString()!;

                        if (rDict.TryGetValue("Building", out var rg) && rg != null) targetNamaGedung = rg.ToString()!;
                        else if (rDict.TryGetValue("rua_gedung", out var rg2) && rg2 != null) targetNamaGedung = rg2.ToString()!;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RUA_MST_GetDetail SP warning");
                }

                string logMessage = $"{targetNamaRuangan} ({targetNamaGedung})";

                var logParams = new
                {
                    p_dev_id = devId,
                    p_use_id = username,
                    p_lok_mac_address = mac,
                    p_lok_perintah = logMessage,
                    p_lok_status = "LocationChange",
                    p_created_by = username
                };

                await conn.ExecuteAsync("LOK_TRX_Create", logParams, commandType: CommandType.StoredProcedure);

                if (!string.Equals(deviceId, mac, StringComparison.OrdinalIgnoreCase))
                {
                    var logParamsStringId = new
                    {
                        p_dev_id = devId,
                        p_use_id = username,
                        p_lok_mac_address = deviceId,
                        p_lok_perintah = logMessage,
                        p_lok_status = "LocationChange",
                        p_created_by = username
                    };
                    await conn.ExecuteAsync("LOK_TRX_Create", logParamsStringId, commandType: CommandType.StoredProcedure);
                }

                return true;
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
                parameters.Add("@p_rua_peruntukan", "Ruang Operasional IoT");
                parameters.Add("@p_rua_luas", 50);
                parameters.Add("@p_rua_kapasitas", 30);
                parameters.Add("@p_rua_gambar", "");
                parameters.Add("@p_rua_allow_pinjam", true);
                parameters.Add("@p_rua_fasilitas", "AC, Lampu, Sensor IoT");
                parameters.Add("@p_rua_barcode", "");
                parameters.Add("@p_created_by", username);

                await conn.ExecuteAsync("RUA_MST_Create", parameters, commandType: CommandType.StoredProcedure);

                var newId = await conn.QueryFirstOrDefaultAsync<int>(
                    "RUA_MST_GetIdByName",
                    new { nama = dto.Ruangan },
                    commandType: CommandType.StoredProcedure
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
                    "RUA_MST_GetDetail", new { id = dto.Id }, commandType: CommandType.StoredProcedure);

                if (detail == null) return false;

                var parameters = new DynamicParameters();
                parameters.Add("@p_rua_id", dto.Id);
                parameters.Add("@p_jru_id", (int)(detail.jru_id ?? 1));
                parameters.Add("@p_rua_nama", dto.Ruangan);
                parameters.Add("@p_rua_gedung", dto.Gedung);
                parameters.Add("@p_rua_lantai", int.TryParse(dto.Lantai, out int l) ? l : (int)(detail.rua_lantai ?? 1));
                parameters.Add("@p_rua_peruntukan", (string)(detail.rua_peruntukan ?? ""));
                parameters.Add("@p_rua_luas", (int)(detail.rua_luas ?? 50));
                parameters.Add("@p_rua_kapasitas", (int)(detail.rua_kapasitas ?? 30));
                parameters.Add("@p_rua_gambar", (string)(detail.rua_gambar ?? ""));
                parameters.Add("@p_rua_allow_pinjam", (bool)(detail.rua_allow_pinjam ?? true));
                parameters.Add("@p_rua_fasilitas", (string)(detail.rua_fasilitas ?? ""));
                parameters.Add("@p_rua_barcode", (string)(detail.rua_barcode ?? ""));
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
                    "RUA_MST_ToggleStatus",
                    new { id, username },
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
                var result = await conn.QueryAsync<dynamic>("RUA_MST_GetAll", commandType: CommandType.StoredProcedure);

                var list = result.Select(row =>
                {
                    IDictionary<string, object> dict = (IDictionary<string, object>)row;

                    int rId = dict.TryGetValue("rua_id", out var ruaIdValue) && ruaIdValue != null
                        ? Convert.ToInt32(ruaIdValue)
                        : dict.TryGetValue("Id", out var idValue) && idValue != null
                            ? Convert.ToInt32(idValue)
                            : 0;

                    string rGedung = dict.TryGetValue("rua_gedung", out var gedungValue) && gedungValue != null
                        ? Convert.ToString(gedungValue)!
                        : dict.TryGetValue("Building", out var buildingValue) && buildingValue != null
                            ? Convert.ToString(buildingValue)!
                            : "Gedung Utama";

                    string rLantai = dict.TryGetValue("rua_lantai", out var lantaiValue) && lantaiValue != null
                        ? Convert.ToString(lantaiValue)!
                        : "1";

                    string rNama = dict.TryGetValue("rua_nama", out var namaValue) && namaValue != null
                        ? Convert.ToString(namaValue)!
                        : dict.TryGetValue("Name", out var nameValue) && nameValue != null
                            ? Convert.ToString(nameValue)!
                            : "Ruangan";

                    string rStatus = dict.TryGetValue("rua_status", out var statusValue) && statusValue != null
                        ? Convert.ToString(statusValue)!
                        : dict.TryGetValue("Status", out var statusValue2) && statusValue2 != null
                            ? Convert.ToString(statusValue2)!
                            : "Aktif";

                    return new Location
                    {
                        Id = rId,
                        Gedung = rGedung,
                        Lantai = rLantai,
                        Ruangan = rNama,
                        Status = rStatus,
                        ActiveDeviceCount = 0
                    };
                }).ToList();

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
                var result = await conn.QueryAsync<dynamic>(
                    "IOT_SP_DashboardLatest_Get",
                    new { Username = username },
                    commandType: CommandType.StoredProcedure
                );

                if (result == null) return [];
                return [.. result.Select(row => {
                    decimal? limitMin = null;
                    decimal? limitMax = null;
                    string category = row.dev_type != null ? Convert.ToString(row.dev_type)! : "";
                    string name = row.dev_nama != null ? Convert.ToString(row.dev_nama)! : "";
                    string mac = row.dev_mac_address != null ? Convert.ToString(row.dev_mac_address)! : "";
                    string stringId = DeviceMapping.GetStringId(mac);

                    string catLower = (category ?? "").ToLower();
                    string nameLower = (name ?? "").ToLower();
                    string idLower = (stringId ?? "").ToLower();

                    if (catLower.Contains("suhu") || catLower.Contains("temp") || catLower.Contains("sensor") || nameLower.Contains("suhu") || nameLower.Contains("temp") || idLower.Contains("temp"))
                    {
                        limitMin = row.LatestValue != null ? Convert.ToDecimal(row.LatestValue) : null;
                        limitMax = row.LatestHumidity != null ? Convert.ToDecimal(row.LatestHumidity) : null;
                    }
                    else if (catLower.Contains("power") || catLower.Contains("listrik") || catLower.Contains("plug") || catLower.Contains("ac") || nameLower.Contains("power") || nameLower.Contains("listrik") || idLower.Contains("pwr"))
                    {
                        limitMin = row.LatestPower != null ? Convert.ToDecimal(row.LatestPower) : (row.LatestValue != null ? Convert.ToDecimal(row.LatestValue) : null);
                    }
                    else if (catLower.Contains("water") || catLower.Contains("air") || nameLower.Contains("air") || idLower.Contains("wat"))
                    {
                        limitMin = row.LatestWater != null ? Convert.ToDecimal(row.LatestWater) : null;
                    }

                    string status = row.PowerStatus != null ? Convert.ToString(row.PowerStatus)! : "OFF";
                    string devStatus = row.dev_status != null ? Convert.ToString(row.dev_status)! : "Aktif";

                    return new Device
                    {
                        Id = stringId ?? string.Empty,
                        MacAddress = mac,
                        Name = name ?? string.Empty,
                        Category = MapCategoryForFrontend(category ?? string.Empty, stringId ?? string.Empty),
                        Status = status,
                        LimitMin = limitMin,
                        LimitMax = limitMax,
                        IsActive = !devStatus.Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase),
                        LocationId = row.rua_id != null ? Convert.ToInt32(row.rua_id) : 1,
                        Ruangan = row.rua_nama != null ? Convert.ToString(row.rua_nama)! : "Lab IoT",
                        Gedung = row.rua_gedung != null ? Convert.ToString(row.rua_gedung)! : "Gedung Utama",
                        Lantai = row.rua_lantai != null ? Convert.ToString(row.rua_lantai)! : "1"
                    };
                })];
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
                var resSp = await conn.QueryFirstOrDefaultAsync<DashboardStats>(
                    "DAS_TRX_GetStats",
                    commandType: CommandType.StoredProcedure
                );

                return resSp ?? new DashboardStats();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data statistik dashboard.");
                return new DashboardStats();
            }
        }

        public async Task<IEnumerable<GraphPoint>> GetDashboardGraphAverageAsync(string category, string period, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                DateTime? adjustedStartDate = startDate?.Date;
                DateTime? adjustedEndDate = endDate?.Date.AddDays(1).AddTicks(-1);

                var result = await conn.QueryAsync<dynamic>(
                    "IOT_SP_GraphAverage_Get",
                    new { Category = category, Period = period, StartDate = adjustedStartDate, EndDate = adjustedEndDate },
                    commandType: CommandType.StoredProcedure
                );
                if (result == null) return [];
                return [.. result.Select(row => new GraphPoint
                {
                    AverageValue = (decimal)(row.AverageValue ?? 0m),
                    MaximumValue = (decimal)(row.MaximumValue ?? 0m),
                    MinimumValue = (decimal)(row.MinimumValue ?? 0m),
                    LabelText = (string)(row.LabelText ?? "00:00")
                })];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data grafik rata-rata: {Category}", category);
                return [];
            }
        }

        public async Task<IEnumerable<GraphPoint>> GetDashboardGraphAsync(string deviceId, string period, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                DateTime? adjustedStartDate = startDate?.Date;
                DateTime? adjustedEndDate = endDate?.Date.AddDays(1).AddTicks(-1);

                var result = await conn.QueryAsync<dynamic>(
                    "IOT_SP_Graph_Get",
                    new { DeviceId = deviceId, Period = period, StartDate = adjustedStartDate, EndDate = adjustedEndDate },
                    commandType: CommandType.StoredProcedure
                );
                if (result == null) return [];
                return [.. result.Select(row => new GraphPoint
                {
                    AverageValue = (decimal)(row.AverageValue ?? 0m),
                    MaximumValue = (decimal)(row.MaximumValue ?? 0m),
                    MinimumValue = (decimal)(row.MinimumValue ?? 0m),
                    LabelText = (string)(row.LabelText ?? "00:00")
                })];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan data grafik: {DeviceId}", deviceId);
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
                return [..result.Select(row => new Notification
                {
                    Id = (long)row.not_id,
                    DeviceId = (string)row.dev_id,
                    DeviceName = (string)row.dev_name,
                    Message = (string)row.not_message,
                    Status = (string)row.not_status,
                    TriggeredDate = (DateTime)row.not_triggered_date
                })];
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
                await using var conn = new SqlConnection(_conn);
                var result = await conn.QueryAsync<dynamic>(
                    "IOT_SP_GraphByDate_Get",
                    new { DeviceId = deviceId, SelectedDate = selectedDate },
                    commandType: CommandType.StoredProcedure
                );

                if (result == null) return [];

                return [..result.Select(row => new GraphPoint
                {
                    AverageValue = (decimal)(row.AverageValue ?? 0m),
                    MaximumValue = (decimal)(row.MaximumValue ?? 0m),
                    MinimumValue = (decimal)(row.MinimumValue ?? 0m),
                    LabelText = (string)(row.LabelText ?? "00:00")
                })];
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
                string lookupMac = DeviceMapping.GetMac(id);
                await using var conn = new SqlConnection(_conn);
                await conn.ExecuteAsync(
                    "DEV_MST_Delete",
                    new { id, lookupMac, username },
                    commandType: CommandType.StoredProcedure
                );

                return true;
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
                var users = await conn.QueryAsync<AppUser>(
                    "USR_MST_GetAll",
                    new { keyword, roleId, status },
                    commandType: CommandType.StoredProcedure
                );

                var userList = (users ?? []).ToList();

                foreach (var u in userList)
                {
                    if (!string.IsNullOrEmpty(u.Username) && UserService.PhotoCache.TryGetValue(u.Username.Trim(), out var p))
                    {
                        u.Photo = p;
                    }
                }
                
                var roles = userList.GroupBy(u => new { u.RoleId, u.RoleName })
                                 .Select(g => new RoleCount { RoleId = g.Key.RoleId ?? "", RoleName = g.Key.RoleName ?? "", Count = g.Count() })
                                 .ToList();

                return new UserListResponse
                {
                    Users = userList,
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
                var userObj = await conn.QueryFirstOrDefaultAsync<AppUser>(
                    "USR_MST_GetDetail",
                    new { username = username?.Trim() },
                    commandType: CommandType.StoredProcedure
                );

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
                await conn.ExecuteAsync(
                    "USR_MST_SetStatus",
                    new { username = username.Trim(), status = string.IsNullOrEmpty(status) ? "Aktif" : status, updatedBy = updatedBy ?? "admin" },
                    commandType: CommandType.StoredProcedure
                );
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status pengguna: {Username}", username);
                return false;
            }
        }

        public async Task<bool> CreateUserAsync(string username, string roleInput, string status, string createdBy, string? email = null, string? password = null, string? photo = null)
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
                string safeCreator = string.IsNullOrEmpty(createdBy) ? "admin" : createdBy;

                string? passwordHash = !string.IsNullOrWhiteSpace(password)
                    ? BCrypt.Net.BCrypt.HashPassword(password)
                    : BCrypt.Net.BCrypt.HashPassword("123456");

                await conn.ExecuteAsync(
                    "USE_MST_Create",
                    new
                    {
                        p_use_id = username.Trim(),
                        p_rol_id = dbRoleId,
                        p_app_id = "APP01",
                        p_use_email = email?.Trim(),
                        p_use_password = passwordHash,
                        p_use_created_by = safeCreator
                    },
                    commandType: CommandType.StoredProcedure
                );

                if (!string.IsNullOrWhiteSpace(photo))
                {
                    UserService.PhotoCache[username.Trim()] = photo; 
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pengguna IoT baru: {Username}", username);
                return false;
            }
        }

        public async Task<bool> UpdateUserAsync(string username, string roleInput, string status, string updatedBy, string? email = null, string? password = null, string? photo = null)
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

                string? passwordHash = !string.IsNullOrWhiteSpace(password)
                    ? BCrypt.Net.BCrypt.HashPassword(password)
                    : null;

                await conn.ExecuteAsync(
                    "USE_MST_Update",
                    new
                    {
                        p_use_id = username.Trim(),
                        p_rol_id = dbRoleId,
                        p_app_id = "APP01",
                        p_use_status = safeStatus,
                        p_use_email = email?.Trim(),
                        p_use_password = passwordHash,
                        p_use_modified_by = safeUpdater
                    },
                    commandType: CommandType.StoredProcedure
                );

                string key = username.Trim();
                if (!string.IsNullOrWhiteSpace(photo))
                {
                    UserService.PhotoCache[key] = photo;   
                }
                else
                {
                    UserService.PhotoCache.Remove(key);     
                }

                return true;
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
                await conn.ExecuteAsync(
                    "USR_MST_Delete",
                    new { username = username.Trim() },
                    commandType: CommandType.StoredProcedure
                );
                return true;
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
                var result = await conn.QueryFirstOrDefaultAsync<ReportSummary>(
                    "IOT_SP_ReportSummary_Get",
                    new { Period = period },
                    commandType: CommandType.StoredProcedure
                );

                return result ?? new ReportSummary { Period = period };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil report summary, period: {Period}", period);
                return new ReportSummary { Period = period };
            }
        }

        public Task<AcSettings?> GetAcSettingsAsync(string deviceId)
        {
            if (_acSettingsCache.TryGetValue(deviceId, out var cached))
                return Task.FromResult<AcSettings?>(cached);

            return Task.FromResult<AcSettings?>(
                new AcSettings
                {
                    Temperature = 22,
                    Mode = "Cool",
                    FanSpeed = "Auto",
                    SwingMode = "Auto"
                });
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
                await using var conn = new SqlConnection(_conn);
                await conn.ExecuteAsync(
                    "USR_MST_UpdateUsername",
                    new { currentUsername, newUsername },
                    commandType: CommandType.StoredProcedure
                );

                if (!string.IsNullOrWhiteSpace(currentUsername) && !string.IsNullOrWhiteSpace(newUsername))
                {
                    string oldKey = currentUsername.Trim();
                    string newKey = newUsername.Trim();
                    if (UserService.PhotoCache.TryGetValue(oldKey, out var photo) && !string.IsNullOrEmpty(photo))
                    {
                        UserService.PhotoCache[newKey] = photo;
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
