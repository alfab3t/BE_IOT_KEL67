#pragma warning disable CS0618 // Disable obsolete warnings from FirebaseAdmin library

using System.IO;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using astratech_apps_backend.Helpers;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;

namespace astratech_apps_backend.Services.Implementations
{
    public class FirebaseService
    {
        private readonly string _connSia;
        private readonly ILogger<FirebaseService> _logger;
        private readonly bool _initialized = false;

        public FirebaseService(DatabaseConfig dbConfig, ILogger<FirebaseService> logger)
        {
            _connSia = dbConfig.ConnectionStringSIA;
            _logger = logger;

            // 1. Inisialisasi Tabel Token di SQL Server
            try
            {
                using var conn = new SqlConnection(_connSia);
                string createTableSql = @"
                    IF OBJECT_ID('dbo.IOT_MST_UserToken', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.IOT_MST_UserToken (
                            username VARCHAR(100) NOT NULL,
                            token VARCHAR(255) NOT NULL,
                            updated_date DATETIME NOT NULL CONSTRAINT DF_token_updated DEFAULT GETDATE(),
                            CONSTRAINT PK_IOT_MST_UserToken PRIMARY KEY (username, token)
                        );
                    END";
                conn.Execute(createTableSql);
                _logger.LogInformation("Tabel dbo.IOT_MST_UserToken dipastikan telah siap.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Gagal menginisialisasi tabel token IOT_MST_UserToken.");
            }

            // 2. Inisialisasi Firebase Admin SDK
            try
            {
                string configPath = Path.Combine(Directory.GetCurrentDirectory(), "firebase-config.json");
                if (File.Exists(configPath))
                {
                    if (FirebaseApp.DefaultInstance == null)
                    {
                        FirebaseApp.Create(new AppOptions()
                        {
                            Credential = GoogleCredential.FromFile(configPath)
                        });
                        _logger.LogInformation("=== FIREBASE ADMIN SDK BERHASIL DIINISIALISASI ===");
                    }
                    _initialized = true;
                }
                else
                {
                    _logger.LogWarning("File firebase-config.json tidak ditemukan di root directory C#. Push notification Firebase dinonaktifkan.");
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Gagal menginisialisasi Firebase Admin SDK.");
            }
        }

        public async Task SaveTokenAsync(string username, string token)
        {
            try
            {
                using var conn = new SqlConnection(_connSia);
                string upsertSql = @"
                    IF EXISTS (SELECT 1 FROM dbo.IOT_MST_UserToken WHERE username = @Username AND token = @Token)
                    BEGIN
                        UPDATE dbo.IOT_MST_UserToken 
                        SET updated_date = GETDATE() 
                        WHERE username = @Username AND token = @Token;
                    END
                    ELSE
                    BEGIN
                        INSERT INTO dbo.IOT_MST_UserToken (username, token, updated_date)
                        VALUES (@Username, @Token, GETDATE());
                    END";

                await conn.ExecuteAsync(upsertSql, new { Username = username, Token = token });
                _logger.LogInformation("SUKSES: Token Firebase untuk user [{Username}] disimpan ke database.", username);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan token Firebase untuk user [{Username}]", username);
            }
        }

        public async Task SendPushNotificationToDeviceUsersAsync(string deviceId, string title, string body)
        {
            if (!_initialized) return;

            try
            {
                using var conn = new SqlConnection(_connSia);
                
                // Ambil semua token HP milik user yang memiliki akses ke device ini,
                // ATAU ambil semua token jika user tersebut adalah ROL001 (Admin)
                string query = @"
                    SELECT DISTINCT ut.token 
                    FROM dbo.IOT_MST_UserToken ut
                    WHERE EXISTS (
                        SELECT 1 FROM dbo.IOT_TRX_UserDeviceAccess uda 
                        WHERE uda.dev_id = @DeviceId AND uda.acc_username = ut.username
                    ) OR EXISTS (
                        SELECT 1 FROM dbo.MST_User u 
                        WHERE u.usr_id = ut.username 
                          AND u.rol_id = 'ROL001' 
                          AND u.app_id = 'APP09'
                    )";

                var tokens = (await conn.QueryAsync<string>(query, new { DeviceId = deviceId })).ToList();

                if (tokens.Count == 0)
                {
                    _logger.LogInformation("Tidak ada push token aktif untuk pengiriman alert device [{DeviceId}].", deviceId);
                    return;
                }

                foreach (var token in tokens)
                {
                    await SendPushNotificationAsync(token, title, body);
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Gagal mengirimkan push notification untuk device [{DeviceId}]", deviceId);
            }
        }

        public async Task CheckAndSendTelemetryAlertAsync(string deviceId)
        {
            if (!_initialized) return;

            try
            {
                using var conn = new SqlConnection(_connSia);
                // Cek apakah ada notifikasi batas sensor (breach) baru yang dimasukkan oleh stored procedure
                var recentNotif = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT TOP 1 not_message 
                      FROM dbo.IOT_TRX_Notification 
                      WHERE dev_id = @dev_id 
                        AND not_triggered_date >= DATEADD(second, -5, GETDATE()) 
                      ORDER BY not_triggered_date DESC",
                    new { dev_id = deviceId }
                );

                if (recentNotif != null)
                {
                    string messageText = recentNotif.not_message;
                    await SendPushNotificationToDeviceUsersAsync(deviceId, "Peringatan Sensor!", messageText);
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Gagal mengecek atau mengirim alert telemetri untuk device {DeviceId}", deviceId);
            }
        }

        private async Task SendPushNotificationAsync(string token, string title, string body)
        {
            try
            {
                var message = new Message()
                {
                    Token = token,
                    Notification = new Notification()
                    {
                        Title = title,
                        Body = body
                    },
                    Data = new Dictionary<string, string>()
                    {
                        { "screen", "Notifications" }
                    }
                };

                string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                _logger.LogInformation("[FIREBASE PUSH SUCCESS] Token: {Token} | Response: {Response}", token.Substring(0, Math.Min(token.Length, 15)) + "...", response);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Gagal mengirim push notification ke token {Token}", token);
            }
        }
    }
}
