using System.Data;
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
                    if (double.TryParse(payload, out val))
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

                // Logika lama (Static mapping) untuk kompatibilitas ke belakang
                double valueOld = 0.0;
                bool isNumeric = double.TryParse(payload, out valueOld);

                if ("valTemp0011".Equals(topic) || "valTempS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        string targetId = "valTempS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                        await SaveTelemetryToDatabaseAsync(targetId, valueOld, "Temperature");
                    }
                }
                else if ("valHumi00112".Equals(topic) || "valHumiS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        string targetId = "valHumiS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                        await SaveTelemetryToDatabaseAsync(targetId, valueOld, "Humidity");
                    }
                }
                else if ("setPower00121".Equals(topic) || "setPowerS3".Equals(topic))
                {
                    string status = "OFF";
                    if (payload == "1" || payload.Equals("ON", StringComparison.OrdinalIgnoreCase) || payload == "1.0")
                    {
                        status = "ON";
                    }
                    string targetId = "setPowerS3".Equals(topic) ? "DEV_AC01" : "DEV_LAMP01";
                    await UpdateDeviceStatusAsync(targetId, status);
                }
                else if ("setTemp00122".Equals(topic) || "setTempS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        string targetId = "setTempS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                        await UpdateDeviceLimitMinAsync(targetId, valueOld);
                    }
                }
                else if ("setMode00124".Equals(topic) || "setModeS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        string targetId = "setModeS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                        await SaveTelemetryToDatabaseAsync(targetId, valueOld, "Mode");
                    }
                }
                else if ("setFan00123".Equals(topic) || "setFanS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        string targetId = "setFanS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                        await SaveTelemetryToDatabaseAsync(targetId, valueOld, "FanSpeed");
                    }
                }
                else if ("setSwing00125".Equals(topic) || "setSwingS3".Equals(topic))
                {
                    if (isNumeric)
                    {
                        string targetId = "setSwingS3".Equals(topic) ? "DEV_TEMP02" : "DEV_TEMP01";
                        await SaveTelemetryToDatabaseAsync(targetId, valueOld, "SwingDirection");
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
                var deviceId = await conn.QueryFirstOrDefaultAsync<string>(
                    "SELECT dev_id FROM dbo.IOT_MST_Device WHERE dev_mac_address = @Mac", 
                    new { Mac = mac });

                if (!string.IsNullOrEmpty(deviceId))
                {
                    var parameters = new
                    {
                        dev_id = deviceId,
                        tel_value = value,
                        tel_type = type
                    };
                    await conn.ExecuteAsync("IOT_SP_Telemetry_Insert", parameters, commandType: CommandType.StoredProcedure);
                    _logger.LogInformation("SUKSES: Telemetri [{Type}] device [{DeviceId}] ({Mac}) bernilai {Value} tersimpan ke database.", type, deviceId, mac, value);
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
                var deviceId = await conn.QueryFirstOrDefaultAsync<string>(
                    "SELECT dev_id FROM dbo.IOT_MST_Device WHERE dev_mac_address = @Mac", 
                    new { Mac = mac });

                if (!string.IsNullOrEmpty(deviceId))
                {
                    var parameters = new
                    {
                        Id = deviceId,
                        Status = status,
                        UpdatedBy = "MQTT_Daemon"
                    };
                    await conn.ExecuteAsync("IOT_SP_Device_SetStatus", parameters, commandType: CommandType.StoredProcedure);
                    _logger.LogInformation("SUKSES: Status device [{DeviceId}] ({Mac}) diubah menjadi {Status}.", deviceId, mac, status);
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
                await using var conn = new SqlConnection(_connSia);
                var parameters = new
                {
                    dev_id = deviceId,
                    tel_value = value,
                    tel_type = type
                };
                await conn.ExecuteAsync("IOT_SP_Telemetry_Insert", parameters, commandType: CommandType.StoredProcedure);
                _logger.LogInformation("SUKSES: Telemetri [{Type}] device [{DeviceId}] bernilai {Value} tersimpan ke database.", type, deviceId, value);
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
                await using var conn = new SqlConnection(_connSia);
                var parameters = new
                {
                    Id = deviceId,
                    Status = status,
                    UpdatedBy = "MQTT_Daemon"
                };
                await conn.ExecuteAsync("IOT_SP_Device_SetStatus", parameters, commandType: CommandType.StoredProcedure);
                _logger.LogInformation("SUKSES: Status device [{DeviceId}] diubah menjadi {Status}.", deviceId, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status device [{DeviceId}] ke {Status}", deviceId, status);
            }
        }

        private async Task UpdateDeviceLimitMinAsync(string deviceId, double limitMin)
        {
            try
            {
                await using var conn = new SqlConnection(_connSia);
                var sql = "UPDATE dbo.IOT_MST_Device SET dev_limit_min = @LimitMin, dev_updated_by = 'MQTT_Daemon', dev_updated_date = GETDATE() WHERE dev_id = @DeviceId";
                await conn.ExecuteAsync(sql, new { LimitMin = limitMin, DeviceId = deviceId });
                _logger.LogInformation("SUKSES: Batas minimum (limit_min) device [{DeviceId}] diubah menjadi {LimitMin}.", deviceId, limitMin);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memperbarui batas minimum device [{DeviceId}]", deviceId);
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
