using astratech_apps_backend.DTOs.Iot.Request;
using astratech_apps_backend.Helpers;
using astratech_apps_backend.Repositories.Interfaces;
using astratech_apps_backend.Services.Implementations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace astratech_apps_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public sealed class IotController(IIotRepository repo, MqttBackgroundService mqttService) : ControllerBase
    {
        private readonly IIotRepository _repo = repo;
        private readonly MqttBackgroundService _mqttService = mqttService;

        private string GetCurrentUsername()
        {
            var username = User.FindFirstValue("namaakun") 
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier) 
                ?? User.FindFirstValue(ClaimTypes.Name) 
                ?? User.FindFirstValue("sub") 
                ?? User.Identity?.Name;
            return string.IsNullOrEmpty(username) ? "admin" : username;
        }


        [HttpGet("devices")]
        [RequiresPermission("perangkat_iot.view")]
        public async Task<IActionResult> GetAllDevices([FromQuery] GetAllDevicesRequest dto)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var (list, totalData) = await _repo.GetAllDevicesAsync(dto);
            var response = new 
            {
                Data = list,
                TotalData = totalData,
                TotalHalaman = ((totalData - 1) / dto.PageSize) + 1
            };

            return Ok(response);
        }

        [HttpGet("devices/{id}")]
        [RequiresPermission("perangkat_iot.view")]
        public async Task<IActionResult> GetDeviceById(string id)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var device = await _repo.GetDeviceByIdAsync(id);
            if (device == null) return NotFound(new { message = "Device tidak ditemukan." });

            return Ok(device);
        }

        [HttpPost("devices")]
        [RequiresPermission("perangkat_iot.create")]
        public async Task<IActionResult> CreateDevice([FromBody] CreateDeviceRequest dto)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var newId = await _repo.CreateDeviceAsync(dto, username);
            if (string.IsNullOrEmpty(newId)) 
                return BadRequest(new { message = "Gagal menambahkan device baru." });

            return Ok(new { message = "Device berhasil disimpan.", id = newId });
        }

        [HttpPut("devices")]
        public async Task<IActionResult> UpdateDevice([FromBody] UpdateDeviceRequest dto)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.UpdateDeviceAsync(dto, username);
            if (!success) return NotFound(new { message = "Device tidak ditemukan." });

            return Ok(new { message = "Device berhasil diupdate." });
        }

        [HttpPatch("devices/{id}/status")]
        public async Task<IActionResult> SetDeviceStatus(string id, [FromQuery] string? statusQuery)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            string status = statusQuery ?? "";
            if (string.IsNullOrEmpty(status))
            {
                using var reader = new StreamReader(Request.Body);
                var rawBody = await reader.ReadToEndAsync();
                status = rawBody.Trim();
                if (status.StartsWith('{'))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(status);
                        if (doc.RootElement.TryGetProperty("status", out var stProp))
                            status = stProp.GetString() ?? status;
                        else if (doc.RootElement.TryGetProperty("Status", out var stProp2))
                            status = stProp2.GetString() ?? status;
                    }
                    catch { }
                }
                status = status.Replace("\"", "").Trim();
            }

            if (string.IsNullOrEmpty(status))
            {
                status = "OFF";
            }
            var success = await _repo.ToggleDeviceStatusAsync(id, status, username);
            if (!success) return NotFound(new { message = "Device tidak ditemukan." });

            await BroadcastDeviceControlMqttAsync(id, status);

            return Ok(new { message = "Status device berhasil diubah." });
        }

        [HttpPatch("devices/{id}/active")]
        public async Task<IActionResult> ToggleDeviceActive(string id, [FromBody] bool isActive)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.ToggleDeviceActiveAsync(id, isActive, username);
            if (!success) return NotFound(new { message = "Device tidak ditemukan." });

            return Ok(new { message = isActive ? "Device berhasil diaktifkan." : "Device berhasil dinonaktifkan." });
        }

        private async Task BroadcastDeviceControlMqttAsync(string id, string status)
        {
            var device = await _repo.GetDeviceByIdAsync(id);
            string mac = device?.MacAddress ?? "";
            string payloadDigit = (status == "ON" || status == "1" || status == "1.0" || status.Equals("Aktif", StringComparison.OrdinalIgnoreCase)) ? "1" : "0";
            string payloadText = payloadDigit == "1" ? "ON" : "OFF";

            string cat = (device?.Category ?? "").ToLower();
            string devName = (device?.Name ?? "").ToLower();
            string idLower = (id ?? "").ToLower();

            bool isLamp = idLower.Contains("lamp") || cat.Contains("lamp") || devName.Contains("lamp");
            bool isAc = idLower.Contains("ac") || cat.Contains("ac") || devName.Contains("ac");

            if (isLamp)
            {
                await _mqttService.PublishMessageAsync("setPowerLAMP01", payloadDigit);
                await _mqttService.PublishMessageAsync("setPowerLAMP02", payloadDigit);
                await _mqttService.PublishMessageAsync("setPowerLampu", payloadDigit);

                await _mqttService.PublishMessageAsync("setPowerLAMP01", payloadText);
                await _mqttService.PublishMessageAsync("setPowerLAMP02", payloadText);
                await _mqttService.PublishMessageAsync("setPowerLampu", payloadText);

                var lampMacs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "84:F3:EB:B4:51:4F", 
                    "97:89:1D:AD:FE:4A", 
                    "E5:B5:54:7C:5C:18"  
                };
                if (!string.IsNullOrEmpty(mac)) lampMacs.Add(mac);

                foreach (var targetMac in lampMacs)
                {
                    await _mqttService.PublishMessageAsync($"device/{targetMac}/control", payloadDigit);
                    await _mqttService.PublishMessageAsync($"device/{targetMac}/control", payloadText);
                    await _mqttService.PublishMessageAsync($"device/{targetMac}/control/lamp", payloadDigit);
                    await _mqttService.PublishMessageAsync($"device/{targetMac}/control/lamp", payloadText);
                }
            }
            else if (isAc)
            {
                await _mqttService.PublishMessageAsync("setPower00121", payloadDigit);
                await _mqttService.PublishMessageAsync("setPowerS3", payloadDigit);

                await _mqttService.PublishMessageAsync("setPower00121", payloadText);
                await _mqttService.PublishMessageAsync("setPowerS3", payloadText);

                var acMacs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "B8:F8:62:62:1E:84" 
                };
                if (!string.IsNullOrEmpty(mac)) acMacs.Add(mac);

                foreach (var targetMac in acMacs)
                {
                    await _mqttService.PublishMessageAsync($"device/{targetMac}/control", payloadDigit);
                    await _mqttService.PublishMessageAsync($"device/{targetMac}/control", payloadText);
                    await _mqttService.PublishMessageAsync($"device/{targetMac}/control/ac", payloadDigit);
                    await _mqttService.PublishMessageAsync($"device/{targetMac}/control/ac", payloadText);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(mac))
                {
                    await _mqttService.PublishMessageAsync($"device/{mac}/control", payloadDigit);
                    await _mqttService.PublishMessageAsync($"device/{mac}/control", payloadText);
                }
            }
        }

        [HttpPatch("devices/{id}/location")]
        public async Task<IActionResult> SetDeviceLocation(string id)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            using var reader = new StreamReader(Request.Body);
            var rawBody = await reader.ReadToEndAsync();
            int locationId = 1;
            if (int.TryParse(rawBody.Trim().Replace("\"", ""), out int parsedId))
            {
                locationId = parsedId;
            }

            var success = await _repo.UpdateDeviceLocationAsync(id, locationId, username);
            if (!success) return BadRequest(new { message = "Gagal memindahkan lokasi device." });

            return Ok(new { message = "Lokasi device berhasil diperbarui." });
        }


        [HttpGet("locations")]
        public async Task<IActionResult> GetAllLocations([FromQuery] string? keyword, [FromQuery] string? status)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var list = await _repo.GetAllLocationsAsync(keyword, status);
            return Ok(list);
        }

        [HttpPost("locations")]
        [RequiresPermission("lokasi_iot.create")]
        public async Task<IActionResult> CreateLocation([FromBody] CreateLocationRequest dto)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var newId = await _repo.CreateLocationAsync(dto, username);
            if (newId == 0) return BadRequest(new { message = "Gagal menambahkan lokasi baru." });

            return Ok(new { message = "Lokasi berhasil disimpan.", id = newId });
        }

        [HttpPut("locations")]
        [RequiresPermission("lokasi_iot.edit")]
        public async Task<IActionResult> UpdateLocation([FromBody] UpdateLocationRequest dto)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.UpdateLocationAsync(dto, username);
            if (!success) return NotFound(new { message = "Lokasi tidak ditemukan." });

            return Ok(new { message = "Lokasi berhasil diperbarui." });
        }

        [HttpPatch("locations/{id}/status")]
        [RequiresPermission("lokasi_iot.edit")]
        public async Task<IActionResult> SetLocationStatus(int id)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.ToggleLocationStatusAsync(id, username);
            if (!success) return NotFound(new { message = "Lokasi tidak ditemukan." });

            return Ok(new { message = "Status lokasi berhasil diubah." });
        }


        [HttpPost("telemetry/inject")]
        [AllowAnonymous]
        public async Task<IActionResult> InjectTelemetry([FromBody] InsertTelemetryRequest dto)
        {
            var success = await _repo.InsertTelemetryAsync(dto);
            if (!success) return BadRequest(new { message = "Gagal menginjeksi data telemetri." });

            return Ok(new { message = "Data telemetri berhasil diinjeksi." });
        }


        [HttpGet("dashboard/latest")]
        public async Task<IActionResult> GetDashboardLatest()
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var data = await _repo.GetDashboardLatestAsync(username);
            return Ok(data);
        }

        [HttpGet("dashboard/stats")]
        public async Task<IActionResult> GetDashboardStats()
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var data = await _repo.GetDashboardStatsAsync(username);
            return Ok(data);
        }

        [HttpGet("dashboard/graph-average")]
        public async Task<IActionResult> GetDashboardGraphAverage(
        [FromQuery] string category,
        [FromQuery] string period = "Daily",
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var data = await _repo.GetDashboardGraphAverageAsync(category, period, startDate, endDate);
            return Ok(data);
        }

        [HttpGet("dashboard/graph/{deviceId}")]
        public async Task<IActionResult> GetDashboardGraph(
            string deviceId,
            [FromQuery] string period = "Daily",
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var data = await _repo.GetDashboardGraphAsync(deviceId, period, startDate, endDate);
            return Ok(data);
        }


        [HttpPost("access/assign")]
        [RequiresPermission("perangkat_iot.edit")]
        public async Task<IActionResult> AssignAccess([FromQuery] string targetUsername, [FromQuery] string deviceId)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.AssignAccessAsync(targetUsername, deviceId, username);
            if (!success) return BadRequest(new { message = "Gagal memberikan hak akses perangkat." });

            return Ok(new { message = "Akses perangkat berhasil diberikan." });
        }

        [HttpDelete("access/remove")]
        [RequiresPermission("perangkat_iot.edit")]
        public async Task<IActionResult> RemoveAccess([FromQuery] string targetUsername, [FromQuery] string deviceId)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.RemoveAccessAsync(targetUsername, deviceId);
            if (!success) return BadRequest(new { message = "Gagal menghapus hak akses perangkat." });

            return Ok(new { message = "Akses perangkat berhasil dicabut." });
        }


        [HttpGet("notifications")]
        [RequiresPermission("notifikasi_iot.view")]
        public async Task<IActionResult> GetNotifications()
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var data = await _repo.GetNotificationsAsync(username);
            return Ok(data);
        }

        [HttpPatch("notifications/{id}/read")]
        [RequiresPermission("notifikasi_iot.view")]
        public async Task<IActionResult> MarkNotificationRead(long id)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.MarkNotificationAsReadAsync(id);
            if (!success) return NotFound(new { message = "Notifikasi tidak ditemukan." });

            return Ok(new { message = "Notifikasi berhasil ditandai dibaca." });
        }

        [HttpPost("notifications/read-bulk")]
        [RequiresPermission("notifikasi_iot.view")]
        public async Task<IActionResult> MarkNotificationsReadBulk([FromBody] List<long> ids)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            if (ids == null || ids.Count == 0) return BadRequest(new { message = "Tidak ada id yang dikirim." });

            var success = await _repo.MarkNotificationsAsReadBulkAsync(ids);
            if (!success) return BadRequest(new { message = "Gagal menandai notifikasi." });

            return Ok(new { message = "Notifikasi berhasil ditandai dibaca." });
        }

        [HttpPost("notifications/delete-bulk")]
        [RequiresPermission("notifikasi_iot.view")]
        public async Task<IActionResult> DeleteNotificationsBulk([FromBody] List<long> ids)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            if (ids == null || ids.Count == 0) return BadRequest(new { message = "Tidak ada id yang dikirim." });

            var success = await _repo.DeleteNotificationsBulkAsync(ids);
            if (!success) return BadRequest(new { message = "Gagal menghapus notifikasi." });

            return Ok(new { message = "Notifikasi berhasil dihapus." });
        }

        [HttpPost("devices/control-ac")]
        [RequiresPermission("perangkat_iot.control")]
        public async Task<IActionResult> ControlAc([FromQuery] string topic, [FromQuery] string payload)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            await _mqttService.PublishMessageAsync(topic, payload);

            var acMacs = new[] { "24:2C:70:39:65:14", "48:2F:50:5C:ED:2D" };

            if (topic.StartsWith("setPower", StringComparison.OrdinalIgnoreCase))
            {
                string dbStatus = (payload == "1" || payload.Equals("ON", StringComparison.OrdinalIgnoreCase) || payload == "1.0") ? "ON" : "OFF";
                string payloadDigit = dbStatus == "ON" ? "1" : "0";

                await _repo.ToggleDeviceStatusAsync("DEV_AC01", dbStatus, username);
                await _repo.ToggleDeviceStatusAsync("DEV_AC02", dbStatus, username);

                await _mqttService.PublishMessageAsync("setPower00121", payloadDigit);
                await _mqttService.PublishMessageAsync("setPowerS3", payloadDigit);
                await _mqttService.PublishMessageAsync("setPowerAC01", payloadDigit);

                foreach (var mac in acMacs)
                {
                    await _mqttService.PublishMessageAsync($"device/{mac}/control", payloadDigit);
                }
            }
            else if (topic.StartsWith("setTemp", StringComparison.OrdinalIgnoreCase))
            {
                await _mqttService.PublishMessageAsync("setTemp00122", payload);
                await _mqttService.PublishMessageAsync("setTempS3", payload);

                foreach (var mac in acMacs)
                {
                    await _mqttService.PublishMessageAsync($"device/{mac}/control/temp", payload);
                }

                if (double.TryParse(payload, out double tempVal))
                {
                    foreach (var targetId in new[] { "DEV_AC01", "DEV_AC02", "DEV_TEMP01", "DEV_TEMP02" })
                    {
                        var device = await _repo.GetDeviceByIdAsync(targetId);
                        if (device != null)
                        {
                            var updateRequest = new UpdateDeviceRequest
                            {
                                Id = device.Id,
                                Name = device.Name,
                                Category = device.Category,
                                LimitMin = (decimal)tempVal,
                                LimitMax = device.LimitMax ?? 28m,
                                Photo = device.Photo
                            };
                            await _repo.UpdateDeviceAsync(updateRequest, username);
                        }
                    }
                }
            }
            else if (topic.StartsWith("setMode", StringComparison.OrdinalIgnoreCase))
            {
                await _mqttService.PublishMessageAsync("setMode00124", payload);
                await _mqttService.PublishMessageAsync("setModeS3", payload);

                foreach (var mac in acMacs)
                {
                    await _mqttService.PublishMessageAsync($"device/{mac}/control/mode", payload);
                }
            }
            else if (topic.StartsWith("setFan", StringComparison.OrdinalIgnoreCase))
            {
                await _mqttService.PublishMessageAsync("setFan00123", payload);
                await _mqttService.PublishMessageAsync("setFanS3", payload);

                foreach (var mac in acMacs)
                {
                    await _mqttService.PublishMessageAsync($"device/{mac}/control/fan", payload);
                }
            }
            else if (topic.StartsWith("setSwing", StringComparison.OrdinalIgnoreCase))
            {
                await _mqttService.PublishMessageAsync("setSwing00125", payload);
                await _mqttService.PublishMessageAsync("setSwingS3", payload);

                foreach (var mac in acMacs)
                {
                    await _mqttService.PublishMessageAsync($"device/{mac}/control/swing", payload);
                }
            }

            return Ok(new { message = "Perintah AC berhasil dikirim." });
        }

        [HttpGet("dashboard/graph-by-date/{deviceId}")]
        [RequiresPermission("perangkat_iot.view")]
        public async Task<IActionResult> GetDashboardGraphByDate(string deviceId, [FromQuery] string date)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            if (!DateTime.TryParse(date, out DateTime selectedDate))
            {
                return BadRequest(new { message = "Format tanggal salah. Gunakan yyyy-MM-dd." });
            }

            var data = await _repo.GetDashboardGraphByDateAsync(deviceId, selectedDate);
            return Ok(data);
        }

        [HttpDelete("devices/{id}")]
        [RequiresPermission("perangkat_iot.delete")]
        public async Task<IActionResult> DeleteDevice(string id)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.DeleteDeviceAsync(id, username);
            if (!success) return NotFound(new { message = "Device tidak ditemukan atau gagal dihapus." });

            return Ok(new { message = "Device berhasil dihapus." });
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers(
            [FromQuery] string? keyword,
            [FromQuery] string? roleId,
            [FromQuery] string? status)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var list = await _repo.GetAllUsersAsync(keyword, roleId, status);
            return Ok(list);
        }

        [HttpGet("users/{targetUsername}")]
        public async Task<IActionResult> GetUserDetail(string targetUsername)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var user = await _repo.GetUserDetailAsync(targetUsername, username);
            if (user == null) return NotFound(new { message = "Pengguna tidak ditemukan." });

            return Ok(user);
        }

        [HttpPatch("users/{targetUsername}/status")]
        public async Task<IActionResult> SetUserStatus(string targetUsername, [FromBody] string status)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            if (status != "Aktif" && status != "Tidak Aktif")
                return BadRequest(new { message = "Status harus 'Aktif' atau 'Tidak Aktif'." });

            var success = await _repo.SetUserStatusAsync(targetUsername, status, username);
            if (!success) return NotFound(new { message = "Pengguna tidak ditemukan." });

            return Ok(new { message = $"Status pengguna berhasil diubah menjadi {status}." });
        }

        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] SaveUserRequest request)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.RoleId))
                return BadRequest(new { message = "Username dan Role wajib diisi." });

            var success = await _repo.CreateUserAsync(request.Username, request.RoleId, request.Status ?? "Aktif", username, request.Email, request.Password, request.Photo); 
            if (!success) return BadRequest(new { message = "Gagal menambahkan pengguna baru. Kemungkinan username sudah terdaftar." });

            return Ok(new { message = "Pengguna baru berhasil ditambahkan." });
        }

        [HttpPut("users")]
        public async Task<IActionResult> UpdateUser([FromBody] SaveUserRequest request)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.RoleId))
                return BadRequest(new { message = "Username dan Role wajib diisi." });

            var success = await _repo.UpdateUserAsync(request.Username, request.RoleId, request.Status ?? "Aktif", username, request.Email, request.Password, request.Photo); 
            if (!success) return BadRequest(new { message = "Gagal memperbarui data pengguna." });

            return Ok(new { message = "Data pengguna berhasil diperbarui." });
        }

        [HttpDelete("users/{targetUsername}")]
        public async Task<IActionResult> DeleteUser(string targetUsername)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.DeleteUserAsync(targetUsername);
            if (!success) return NotFound(new { message = "Pengguna tidak ditemukan atau gagal dicabut hak aksesnya." });

            return Ok(new { message = "Hak akses pengguna berhasil dicabut." });
        }

        [HttpGet("report")]
        public async Task<IActionResult> GetReportSummary([FromQuery] string period = "today")
        {
            var validPeriods = new[] { "today", "week", "month" };
            if (!validPeriods.Contains(period.ToLower()))
                return BadRequest(new { message = "Period tidak valid. Gunakan: today, week, month." });

            var result = await _repo.GetReportSummaryAsync(period.ToLower());
            return Ok(result);
        }


        [HttpGet("devices/{deviceId}/ac-settings")]
        public async Task<IActionResult> GetAcSettings(string deviceId)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var settings = await _repo.GetAcSettingsAsync(deviceId);
            if (settings == null)
            {
                return Ok(new { temperature = 22, mode = "Cool", fanSpeed = "Auto", swingMode = "Auto" });
            }
            return Ok(settings);
        }

        [HttpPost("devices/{deviceId}/ac-settings")]
        public async Task<IActionResult> SaveAcSettings(string deviceId, [FromBody] SaveAcSettingsRequest dto)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var settings = new astratech_apps_backend.DTOs.Iot.AcSettings
            {
                Temperature = dto.Temperature,
                Mode        = dto.Mode,
                FanSpeed    = dto.FanSpeed,
                SwingMode   = dto.SwingMode,
            };

            var success = await _repo.SaveAcSettingsAsync(deviceId, settings, username);
            if (!success) return BadRequest(new { message = "Gagal menyimpan pengaturan AC." });
            return Ok(new { message = "Pengaturan AC berhasil disimpan." });
        }

        [AllowAnonymous]
        [HttpPut("users/profile/username")]
        [HttpPost("users/profile/username")]
        public async Task<IActionResult> UpdateSelfUsername([FromBody] UpdateUsernameRequest dto)
        {
            var currentUsername = !string.IsNullOrWhiteSpace(dto.CurrentUsername) 
                ? dto.CurrentUsername.Trim() 
                : GetCurrentUsername();

            if (string.IsNullOrEmpty(currentUsername)) currentUsername = "admin";

            if (string.IsNullOrWhiteSpace(dto.NewUsername))
                return BadRequest(new { message = "Username baru tidak boleh kosong." });

            await _repo.UpdateUsernameSelfAsync(currentUsername, dto.NewUsername.Trim());
            return Ok(new { message = "Username berhasil diperbarui di database." });
        }
    }

    public class UpdateUsernameRequest
    {
        public string CurrentUsername { get; set; } = "";
        public string NewUsername { get; set; } = "";
    }

    public class SaveUserRequest
    {
        public string Username { get; set; } = "";
        public string RoleId { get; set; } = "";
        public string? Status { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? Photo { get; set; }
    }

    public class SaveAcSettingsRequest
    {
        public int Temperature { get; set; } = 22;
        public string Mode { get; set; } = "Cool";
        public string FanSpeed { get; set; } = "Auto";
        public string SwingMode { get; set; } = "Auto";
    }
}
