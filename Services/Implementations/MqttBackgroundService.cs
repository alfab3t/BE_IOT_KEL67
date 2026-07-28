using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using astratech_apps_backend.Helpers;
using Dapper;
using Microsoft.Data.SqlClient;
using MQTTnet;
using MQTTnet.Client;

namespace astratech_apps_backend.Services.Implementations
{
    public class MqttBackgroundService : BackgroundService
    {
        private readonly string _connSia;
        private readonly ILogger<MqttBackgroundService> _logger;
        private readonly MqttFactory _mqttFactory;
        private readonly IMqttClient _mqttClient;
        private readonly MqttClientOptions _mqttOptions;

        public MqttBackgroundService(DatabaseConfig dbConfig, ILogger<MqttBackgroundService> logger)
        {
            _connSia = dbConfig.ConnectionStringSIA;
            _logger = logger;

            _mqttFactory = new MqttFactory();
            _mqttClient = _mqttFactory.CreateMqttClient();

            _mqttOptions = new MqttClientOptionsBuilder()
                .WithTcpServer("astratech.id", 1883)
                .WithClientId("CSharp_Backend_MQTT_Daemon_" + Guid.NewGuid().ToString().Substring(0, 5))
                .WithCleanSession()
                .Build();

            // Set event handlers
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=== ASTRATECH MQTT BACKGROUND SERVICE STARTED ===");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_mqttClient.IsConnected)
                    {
                        _logger.LogInformation("Menghubungkan ke MQTT Broker: astratech.id:1883...");
                        await _mqttClient.ConnectAsync(_mqttOptions, stoppingToken);
                        _logger.LogInformation("SUKSES terhubung ke MQTT Broker!");

                        // Subscribe topics
                        var topics = new[] { 
                            "valTemp0011", 
                            "valHumi00112", 
                            "setPower00121", 
                            "setTemp00122", 
                            "setMode00124", 
                            "setFan00123", 
                            "setSwing00125",
                            "device/+/sensor/temperature",
                            "device/+/sensor/humidity",
                            "device/+/status",
                            "PMGedungA",
                            "PMGedungB",
                            "PMGedungC",
                            "PMGedungD",
                            "esp32/water",
                            "valTempS3",
                            "valHumiS3",
                            "setPowerS3",
                            "setTempS3",
                            "setModeS3",
                            "setFanS3",
                            "setSwingS3"
                        };

                        var subscribeOptionsBuilder = _mqttFactory.CreateSubscribeOptionsBuilder();
                        foreach (var topic in topics)
                        {
                            subscribeOptionsBuilder.WithTopicFilter(f => f.WithTopic(topic));
                        }

                        await _mqttClient.SubscribeAsync(subscribeOptionsBuilder.Build(), stoppingToken);
                        _logger.LogInformation("SUKSES men-subscribe semua topik sensor!");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gagal mengkoneksikan atau men-subscribe MQTT Broker. Reconnect dalam 5 detik...");
                }

                await Task.Delay(5000, stoppingToken);
            }
        }

        public async Task PublishMessageAsync(string topic, string payload)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    _logger.LogWarning("Gagal Publish: Client tidak terhubung ke broker MQTT.");
                    return;
                }

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(message, CancellationToken.None);
                _logger.LogInformation("[MQTT PUBLISH SUCCESS] Topik: {Topic} | Payload: {Payload}", topic, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengirimkan pesan MQTT ke topik {Topic}", topic);
            }
        }

        private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            string topic = e.ApplicationMessage.Topic;
            string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment).Trim();

            _logger.LogInformation("[MQTT EVENT] Topik: {Topic} | Payload: {Payload}", topic, payload);

            try
            {
                // Baca data meteran listrik PMGedungA, B, C, D
                if (topic.StartsWith("PMGedung", StringComparison.OrdinalIgnoreCase))
                {
                    string gedungChar = topic.Substring("PMGedung".Length).ToUpper();
                    string targetDevId = $"DEV_PWR_{gedungChar}";

                    using var doc = JsonDocument.Parse(payload);
                    if (doc.RootElement.TryGetProperty("totalkw", out var powerElement))
                    {
                        double kw = powerElement.GetDouble();
                        double watt = kw * 1000.0; // Konversi kW ke Watt agar konsisten dengan DB/UI

                        // Validasi agar tidak terjadi overflow di kolom decimal database
                        if (double.IsNaN(watt) || double.IsInfinity(watt) || watt > 99999999999999.0 || watt < -99999999999999.0)
                        {
                            _logger.LogWarning("Mengabaikan data telemetri PMGedung{GedungChar} karena nilai tidak valid/overflow: {Value} Watt", gedungChar, watt);
                        }
                        else
                        {
                            await SaveTelemetryToDatabaseAsync(targetDevId, watt, "Power");
                            
                            // Juga simpan ke DEV_PWR01 untuk kompabilitas ke belakang bagi Gedung C
                            if (gedungChar == "C")
                            {
                                await SaveTelemetryToDatabaseAsync("DEV_PWR01", watt, "Power");
                            }
                        }
                    }
                    return;
                }

                // Baca data debit air esp32/water
                if ("esp32/water".Equals(topic, StringComparison.OrdinalIgnoreCase))
                {
                    double val = 0.0;
                    if (double.TryParse(payload, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                    {
                        await SaveTelemetryToDatabaseAsync("DEV_WAT01", val, "WaterFlow");
                    }
                    else
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(payload);
                            if (doc.RootElement.TryGetProperty("value", out var valProp))
                            {
                                await SaveTelemetryToDatabaseAsync("DEV_WAT01", valProp.GetDouble(), "WaterFlow");
                            }
                            else if (doc.RootElement.TryGetProperty("water", out var waterProp))
                            {
                                await SaveTelemetryToDatabaseAsync("DEV_WAT01", waterProp.GetDouble(), "WaterFlow");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Gagal memproses payload non-numerik untuk esp32/water: {Payload}", payload);
                        }
                    }
                    return;
                }



                // Cek jika menggunakan pola topik baru device/+/sensor/+ atau device/+/status
                string[] parts = topic.Split('/');
                if (parts.Length >= 3 && "device".Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                {
                    string mac = parts[1];
                    using var doc = JsonDocument.Parse(payload);

                    if (parts.Length == 4 && "sensor".Equals(parts[2], StringComparison.OrdinalIgnoreCase))
                    {
                        string sensorType = parts[3];
                        if (doc.RootElement.TryGetProperty("value", out var valElement))
                        {
                            double value = valElement.GetDouble();
                            string type = "Temperature";
                            if (sensorType.Equals("humidity", StringComparison.OrdinalIgnoreCase))
                            {
                                type = "Humidity";
                            }
                            await SaveTelemetryByMacAsync(mac, value, type);
                        }
                    }
                    else if (parts.Length == 3 && "status".Equals(parts[2], StringComparison.OrdinalIgnoreCase))
                    {
                        if (doc.RootElement.TryGetProperty("state", out var stateElement))
                        {
                            string state = stateElement.GetString() ?? "OFF";
                            await UpdateDeviceStatusByMacAsync(mac, state);
                        }
                    }
                    return;
                }

                // Logika parser payload format: MAC;VALUE (misal: B8:F8:62:62:1E:84;28.7) atau angka murni
                string processedPayload = payload;
                string? parsedMac = null;

                if (payload.Contains(';'))
                {
                    string[] split = payload.Split(';');
                    if (split.Length == 2)
                    {
                        parsedMac = split[0].Trim();
                        processedPayload = split[1].Trim();
                    }
                }

                double valueOld = 0.0;
                bool isNumeric = double.TryParse(processedPayload, NumberStyles.Any, CultureInfo.InvariantCulture, out valueOld);

                if ("valTemp0011".Equals(topic) || "valTempS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        if (!string.IsNullOrEmpty(parsedMac))
                        {
                            await SaveTelemetryByMacAsync(parsedMac, valueOld, "Temperature");
                        }
                        else
                        {
                            string targetId = "valTempS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                            await SaveTelemetryToDatabaseAsync(targetId, valueOld, "Temperature");
                        }
                    }
                }
                else if ("valHumi00112".Equals(topic) || "valHumiS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        if (!string.IsNullOrEmpty(parsedMac))
                        {
                            await SaveTelemetryByMacAsync(parsedMac, valueOld, "Humidity");
                        }
                        else
                        {
                            string targetId = "valHumiS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                            await SaveTelemetryToDatabaseAsync(targetId, valueOld, "Humidity");
                        }
                    }
                }
                else if ("setPower00121".Equals(topic) || "setPowerS3".Equals(topic) || "setPowerAC01".Equals(topic))
                {
                    string status = "OFF";
                    if (processedPayload == "1" || processedPayload.Equals("ON", StringComparison.OrdinalIgnoreCase) || processedPayload == "1.0")
                    {
                        status = "ON";
                    }

                    if (!string.IsNullOrEmpty(parsedMac))
                    {
                        await UpdateDeviceStatusByMacAsync(parsedMac, status);
                    }
                    else
                    {
                        string targetId = "DEV_AC01";
                        await UpdateDeviceStatusAsync(targetId, status);
                    }
                }
                else if ("setTemp00122".Equals(topic) || "setTempS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        if (!string.IsNullOrEmpty(parsedMac))
                        {
                            // Update limit min based on MAC
                            await UpdateDeviceLimitMinByMacAsync(parsedMac, valueOld);
                        }
                        else
                        {
                            string targetId = "setTempS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                            await UpdateDeviceLimitMinAsync(targetId, valueOld);
                        }
                    }
                }
                else if ("setMode00124".Equals(topic) || "setModeS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        if (!string.IsNullOrEmpty(parsedMac))
                        {
                            await SaveTelemetryByMacAsync(parsedMac, valueOld, "Mode");
                        }
                        else
                        {
                            string targetId = "setModeS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                            await SaveTelemetryToDatabaseAsync(targetId, valueOld, "Mode");
                        }
                    }
                }
                else if ("setFan00123".Equals(topic) || "setFanS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        if (!string.IsNullOrEmpty(parsedMac))
                        {
                            await SaveTelemetryByMacAsync(parsedMac, valueOld, "FanSpeed");
                        }
                        else
                        {
                            string targetId = "setFanS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                            await SaveTelemetryToDatabaseAsync(targetId, valueOld, "FanSpeed");
                        }
                    }
                }
                else if ("setSwing00125".Equals(topic) || "setSwingS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        if (!string.IsNullOrEmpty(parsedMac))
                        {
                            await SaveTelemetryByMacAsync(parsedMac, valueOld, "SwingDirection");
                        }
                        else
                        {
                            string targetId = "setSwingS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                            await SaveTelemetryToDatabaseAsync(targetId, valueOld, "SwingDirection");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saat memproses pesan MQTT untuk topik {Topic}", topic);
            }
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            _logger.LogWarning("Koneksi terputus dari MQTT Broker.");
            return Task.CompletedTask;
        }

        private async Task SaveTelemetryByMacAsync(string mac, double value, string type)
        {
            try
            {
                await using var conn = new SqlConnection(_connSia);
                var device = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT dev_id, rua_id FROM MST_Device WHERE dev_mac_address = @mac AND dev_is_deleted = 0",
                    new { mac });

                if (device != null)
                {
                    int devId = (int)device.dev_id;
                    int ruaId = (int)device.rua_id;
                    string unit = type.Contains("Temp") ? "°C" : (type.Contains("Humi") ? "%" : (type.Contains("Power") ? "W" : ""));

                    var parameters = new
                    {
                        p_dev_id = devId,
                        p_rua_id = ruaId,
                        p_das_mac_address = mac,
                        p_das_value = value,
                        p_das_unit = unit,
                        p_das_jenis = type,
                        p_das_timestamp = DateTime.Now,
                        p_das_created_by = "MQTT_Daemon"
                    };
                    await conn.ExecuteAsync("DAS_TRX_Create", parameters, commandType: CommandType.StoredProcedure);
                    _logger.LogInformation("SUKSES: Telemetri [{Type}] device [{DeviceId}] ({Mac}) bernilai {Value} tersimpan ke database.", type, devId, mac, value);
                    await SaveDeviceStatusAsync(devId, mac, "Online");
                }
                else
                {
                    _logger.LogWarning("Gagal menyimpan telemetri: MAC [{Mac}] tidak terdaftar di database.", mac);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan telemetri untuk MAC [{Mac}]", mac);
            }
        }

        private async Task UpdateDeviceStatusByMacAsync(string mac, string status)
        {
            try
            {
                await using var conn = new SqlConnection(_connSia);
                var device = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT dev_id FROM MST_Device WHERE dev_mac_address = @mac AND dev_is_deleted = 0",
                    new { mac });

                if (device != null)
                {
                    int devId = (int)device.dev_id;
                    var parameters = new
                    {
                        p_dev_id = devId,
                        p_use_id = "MQTT_Daemon",
                        p_lok_mac_address = mac,
                        p_lok_perintah = status,
                        p_lok_status = "Success",
                        p_created_by = "MQTT_Daemon"
                    };
                    await conn.ExecuteAsync("LOK_TRX_Create", parameters, commandType: CommandType.StoredProcedure);
                    _logger.LogInformation("SUKSES: Status device [{DeviceId}] ({Mac}) diubah menjadi {Status}.", devId, mac, status);
                    string kondisi = status.Equals("ON", StringComparison.OrdinalIgnoreCase) || status.Equals("Online", StringComparison.OrdinalIgnoreCase) ? "Online" : "Offline";
                    await SaveDeviceStatusAsync(devId, mac, kondisi);
                }
                else
                {
                    _logger.LogWarning("Gagal mengubah status: MAC [{Mac}] tidak terdaftar di database.", mac);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status device untuk MAC [{Mac}] ke {Status}", mac, status);
            }
        }

        private async Task SaveTelemetryToDatabaseAsync(string deviceId, double value, string type)
        {
            try
            {
                string lookupMac = DeviceMapping.GetMac(deviceId);
                await using var conn = new SqlConnection(_connSia);
                
                var device = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT dev_id, rua_id FROM MST_Device WHERE dev_mac_address = @lookupMac OR dev_nama = @deviceId",
                    new { lookupMac, deviceId });

                int devId;
                int ruaId = 1;

                if (device == null)
                {
                    // Auto-register the device if it does not exist in local database
                    await conn.ExecuteAsync(
                        "INSERT INTO MST_Device (rua_id, dev_mac_address, dev_nama, dev_type, dev_status, dev_is_deleted, dev_created_by, dev_created_date) VALUES (1, @lookupMac, @deviceId, @type, 'Aktif', 0, 'MQTT_Daemon', GETDATE())",
                        new { lookupMac, deviceId, type = type == "Power" ? "Power Meter" : "Water Flow Meter" });
                    
                    var newDev = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT dev_id, rua_id FROM MST_Device WHERE dev_mac_address = @lookupMac", new { lookupMac });
                    
                    devId = (int)newDev.dev_id;
                    ruaId = (int)newDev.rua_id;
                    _logger.LogInformation("Auto-registered device: {DeviceId} dengan Int ID: {DevId}", deviceId, devId);
                }
                else
                {
                    devId = (int)device.dev_id;
                    ruaId = (int)device.rua_id;
                }

                string unit = type.Contains("Temp") ? "°C" : (type.Contains("Humi") ? "%" : (type.Contains("Power") ? "W" : ""));

                var parameters = new
                {
                    p_dev_id = devId,
                    p_rua_id = ruaId,
                    p_das_mac_address = deviceId,
                    p_das_value = value,
                    p_das_unit = unit,
                    p_das_jenis = type,
                    p_das_timestamp = DateTime.Now,
                    p_das_created_by = "MQTT_Daemon"
                };
                await conn.ExecuteAsync("DAS_TRX_Create", parameters, commandType: CommandType.StoredProcedure);
                _logger.LogInformation("SUKSES: Telemetri [{Type}] device [{DeviceId}] bernilai {Value} tersimpan ke database.", type, devId, value);
                await SaveDeviceStatusAsync(devId, lookupMac, "Online");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan telemetri [{Type}] untuk device [{DeviceId}]", type, deviceId);
            }
        }

        private async Task UpdateDeviceStatusAsync(string deviceId, string status)
        {
            try
            {
                string lookupMac = DeviceMapping.GetMac(deviceId);
                await using var conn = new SqlConnection(_connSia);
                var device = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT dev_id, dev_mac_address FROM MST_Device WHERE dev_mac_address = @lookupMac OR dev_nama = @deviceId",
                    new { lookupMac, deviceId });

                if (device != null)
                {
                    int devId = (int)device.dev_id;
                    string mac = (string)device.dev_mac_address;
                    var parameters = new
                    {
                        p_dev_id = devId,
                        p_use_id = "MQTT_Daemon",
                        p_lok_mac_address = mac,
                        p_lok_perintah = status,
                        p_lok_status = "Success",
                        p_created_by = "MQTT_Daemon"
                    };
                    await conn.ExecuteAsync("LOK_TRX_Create", parameters, commandType: CommandType.StoredProcedure);
                    _logger.LogInformation("SUKSES: Status device [{DeviceId}] diubah menjadi {Status}.", devId, status);
                    string kondisi = status.Equals("ON", StringComparison.OrdinalIgnoreCase) || status.Equals("Online", StringComparison.OrdinalIgnoreCase) ? "Online" : "Offline";
                    await SaveDeviceStatusAsync(devId, mac, kondisi);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status device [{DeviceId}] ke {Status}", deviceId, status);
            }
        }

        private Task UpdateDeviceLimitMinAsync(string deviceId, double limitMin)
        {
            _logger.LogInformation("Batas minimum device [{DeviceId}] diterima: {LimitMin}. (Diabaikan karena skema database tidak mendukung limit_min)", deviceId, limitMin);
            return Task.CompletedTask;
        }

        private Task UpdateDeviceLimitMinByMacAsync(string mac, double limitMin)
        {
            _logger.LogInformation("Batas minimum device MAC [{Mac}] diterima: {LimitMin}. (Diabaikan karena skema database tidak mendukung limit_min)", mac, limitMin);
            return Task.CompletedTask;
        }

        private async Task SaveDeviceStatusAsync(int devId, string mac, string kondisi)
        {
            try
            {
                await using var conn = new SqlConnection(_connSia);
                var parameters = new
                {
                    p_dev_id = devId,
                    p_stp_mac_address = mac,
                    p_stp_kondisi = kondisi,
                    p_stp_timestamp = DateTime.Now,
                    p_created_by = "MQTT_Daemon"
                };
                await conn.ExecuteAsync("STP_TRX_Create", parameters, commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan status perangkat ke TRX_StatusPerangkat untuk devId {DevId}", devId);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Menghentikan MQTT client...");
            if (_mqttClient != null)
            {
                await _mqttClient.DisconnectAsync(cancellationToken: cancellationToken);
            }
            await base.StopAsync(cancellationToken);
        }
    }
}
