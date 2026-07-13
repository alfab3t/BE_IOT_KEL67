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

        private string? GetCurrentUsername() => User.FindFirstValue("namaakun");


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
        [RequiresPermission("perangkat_iot.edit")]
        public async Task<IActionResult> UpdateDevice([FromBody] UpdateDeviceRequest dto)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.UpdateDeviceAsync(dto, username);
            if (!success) return NotFound(new { message = "Device tidak ditemukan." });

            return Ok(new { message = "Device berhasil diupdate." });
        }

        [HttpPatch("devices/{id}/status")]
        [RequiresPermission("perangkat_iot.control")]
        public async Task<IActionResult> SetDeviceStatus(string id)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            // Baca body secara raw untuk menghindari error JSON parsing
            using var reader = new StreamReader(Request.Body);
            var rawBody = await reader.ReadToEndAsync();
            var status = rawBody.Trim().Replace("\"", ""); // Hapus tanda kutip jika ada

            var success = await _repo.ToggleDeviceStatusAsync(id, status, username);
            if (!success) return NotFound(new { message = "Device tidak ditemukan." });

            // Publish ke MQTT agar perangkat fisik menyalakan/mematikan saklar
            var device = await _repo.GetDeviceByIdAsync(id);
            if (device != null)
            {
                string mac = device.MacAddress;
                // Jika Lampu dan Sensor berada di satu board fisik ESP8266 yang sama (berbagi satu MAC address)
                if (id == "DEV_LAMP01")
                {
                    var tempDevice = await _repo.GetDeviceByIdAsync("DEV_TEMP01");
                    if (tempDevice != null && !string.IsNullOrEmpty(tempDevice.MacAddress))
                    {
                        mac = tempDevice.MacAddress;
                    }
                }
                // Jika AC Panasonic menggunakan board fisik ESP32-S3 yang sama
                else if (id == "DEV_AC01")
                {
                    var tempDevice = await _repo.GetDeviceByIdAsync("DEV_TEMP02");
                    if (tempDevice != null && !string.IsNullOrEmpty(tempDevice.MacAddress))
                    {
                        mac = tempDevice.MacAddress;
                    }
                }

                if (!string.IsNullOrEmpty(mac))
                {
                    string topic = $"device/{mac}/control";
                    await _mqttService.PublishMessageAsync(topic, status);
                }
            }

            return Ok(new { message = "Status device berhasil diubah." });
        }

        [HttpPatch("devices/{id}/active")]
        [RequiresPermission("perangkat_iot.edit")]
        public async Task<IActionResult> ToggleDeviceActive(string id, [FromBody] bool isActive)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.ToggleDeviceActiveAsync(id, isActive, username);
            if (!success) return NotFound(new { message = "Device tidak ditemukan." });

            return Ok(new { message = isActive ? "Device berhasil diaktifkan." : "Device berhasil dinonaktifkan." });
        }

        [HttpPatch("devices/{id}/location")]
        [RequiresPermission("perangkat_iot.edit")]
        public async Task<IActionResult> SetDeviceLocation(string id, [FromBody] int locationId)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.UpdateDeviceLocationAsync(id, locationId, username);
            if (!success) return BadRequest(new { message = "Gagal memindahkan lokasi device." });

            return Ok(new { message = "Lokasi device berhasil diperbarui." });
        }


        [HttpGet("locations")]
        [RequiresPermission("lokasi_iot.view")]
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
        [RequiresPermission("dashboard_iot.view")]
        public async Task<IActionResult> GetDashboardLatest()
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var data = await _repo.GetDashboardLatestAsync(username);
            return Ok(data);
        }

        [HttpGet("dashboard/stats")]
        [RequiresPermission("dashboard_iot.view")]
        public async Task<IActionResult> GetDashboardStats()
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var data = await _repo.GetDashboardStatsAsync(username);
            return Ok(data);
        }

        [HttpGet("dashboard/graph/{deviceId}")]
        [RequiresPermission("dashboard_iot.view")]
        public async Task<IActionResult> GetDashboardGraph(string deviceId, [FromQuery] string period = "Daily")
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var data = await _repo.GetDashboardGraphAsync(deviceId, period);
            return Ok(data);
        }

        [HttpGet("dashboard/graph-average")]
        [RequiresPermission("dashboard_iot.view")]
        public async Task<IActionResult> GetDashboardGraphAverage([FromQuery] string category, [FromQuery] string period = "Daily")
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var data = await _repo.GetDashboardGraphAverageAsync(category, period);
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

            // Sync with Database
            if (topic == "setPower00121" || topic == "setPowerS3")
            {
                string dbStatus = (payload == "1" || payload.Equals("ON", StringComparison.OrdinalIgnoreCase)) ? "ON" : "OFF";
                string targetId = (topic == "setPowerS3") ? "DEV_AC01" : "DEV_LAMP01";
                await _repo.ToggleDeviceStatusAsync(targetId, dbStatus, username);
            }
            else if (topic == "setTemp00122" || topic == "setTempS3")
            {
                if (double.TryParse(payload, out double tempVal))
                {
                    string targetId = (topic == "setTempS3") ? "DEV_TEMP02" : "DEV_TEMP01";
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
        // ── User Management ──────────────────────────────────────────────────

        [HttpGet("users")]
        [RequiresPermission("iot.view")]
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
        [RequiresPermission("iot.view")]
        public async Task<IActionResult> GetUserDetail(string targetUsername)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var user = await _repo.GetUserDetailAsync(targetUsername, username);
            if (user == null) return NotFound(new { message = "Pengguna tidak ditemukan." });

            return Ok(user);
        }

        [HttpPatch("users/{targetUsername}/status")]
        [RequiresPermission("iot.view")]
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
        [RequiresPermission("iot.view")]
        public async Task<IActionResult> CreateUser([FromBody] SaveUserRequest request)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.RoleId))
                return BadRequest(new { message = "Username dan Role wajib diisi." });

            var success = await _repo.CreateUserAsync(request.Username, request.RoleId, request.Status ?? "Aktif", username);
            if (!success) return BadRequest(new { message = "Gagal menambahkan pengguna baru. Kemungkinan username sudah terdaftar." });

            return Ok(new { message = "Pengguna baru berhasil ditambahkan." });
        }

        [HttpPut("users")]
        [RequiresPermission("iot.view")]
        public async Task<IActionResult> UpdateUser([FromBody] SaveUserRequest request)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.RoleId))
                return BadRequest(new { message = "Username dan Role wajib diisi." });

            var success = await _repo.UpdateUserAsync(request.Username, request.RoleId, request.Status ?? "Aktif", username);
            if (!success) return BadRequest(new { message = "Gagal memperbarui data pengguna." });

            return Ok(new { message = "Data pengguna berhasil diperbarui." });
        }

        [HttpDelete("users/{targetUsername}")]
        [RequiresPermission("iot.view")]
        public async Task<IActionResult> DeleteUser(string targetUsername)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var success = await _repo.DeleteUserAsync(targetUsername);
            if (!success) return NotFound(new { message = "Pengguna tidak ditemukan atau gagal dicabut hak aksesnya." });

            return Ok(new { message = "Hak akses pengguna berhasil dicabut." });
        }

        // ─── Report Summary ───────────────────────────────────────────────────
        [HttpGet("report")]
        public async Task<IActionResult> GetReportSummary([FromQuery] string period = "today")
        {
            var validPeriods = new[] { "today", "week", "month" };
            if (!validPeriods.Contains(period.ToLower()))
                return BadRequest(new { message = "Period tidak valid. Gunakan: today, week, month." });

            var result = await _repo.GetReportSummaryAsync(period.ToLower());
            return Ok(result);
        }
    }

    public class SaveUserRequest
    {
        public string Username { get; set; } = "";
        public string RoleId { get; set; } = "";
        public string? Status { get; set; }
    }
}
