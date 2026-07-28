using astratech_apps_backend.DTOs.Auth;
using astratech_apps_backend.Helpers;
using astratech_apps_backend.Services.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace astratech_apps_backend.Services.Implementations
{
    public class UserService(DatabaseConfig dbConfig, ILogger<UserService> logger) : IUserService
    {
        private readonly string _conn = dbConfig.ConnectionStringSSO;
        private readonly ILogger<UserService> _logger = logger;

        public async Task<(bool IsSuccess, List<Aplikasi> ListAplikasi, string? ErrorMessage)> AuthenticateAsync(string username, string jenisAplikasi)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);

                var parameters = new
                {
                    Username = username,
                    JenisAplikasi = jenisAplikasi
                };
                var result = await conn.QueryAsync<dynamic>("ATH_TRX_GetAppByUsername", parameters, commandType: CommandType.StoredProcedure);

                if (!result.Any())
                {
                    return (false, [], "Username atau password tidak valid.");
                }

                // Ambil mapping role khusus IoT dari tabel IOT_TRX_UserRole (jika ada)
                var iotMapping = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "IOT_SP_User_GetIotRole",
                    new { Username = username },
                    commandType: CommandType.StoredProcedure
                );

                var list = result.Select(row => {
                    var appId = (string?)row.app_id ?? "";
                    var roleId = (string?)row.rol_id ?? "";
                    var roleName = (string?)row.rol_deskripsi ?? "";

                    // Override jika aplikasinya IoT (APP09) dan ada mapping khusus
                    if (appId == "APP09" && iotMapping != null)
                    {
                        var mappingDict = iotMapping as System.Collections.Generic.IDictionary<string, object>;
                        if (mappingDict != null)
                        {
                            if (mappingDict.TryGetValue("iot_role_id", out var valId) && valId is string strId)
                            {
                                roleId = strId;
                            }
                            if (mappingDict.TryGetValue("rol_deskripsi", out var valDesc) && valDesc is string strDesc)
                            {
                                roleName = strDesc;
                            }
                        }
                    }

                    return new Aplikasi
                    {
                        NamaAplikasi = (string?)row.app_deskripsi ?? "",
                        NamaRole = roleName,
                        Root = (string?)row.app_tautan ?? "",
                        AppId = appId,
                        RoleId = roleId,
                        AppIcon = (string?)row.app_icon ?? ""
                    };
                }).ToList();

                return (true, list, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan daftar aplikasi. | [{Username}]", username);
                return (false, [], "Gagal mendapatkan daftar aplikasi.");
            }
        }

        public async Task<(bool IsSuccess, List<Menu> ListMenu, string? ErrorMessage)> GetListMenuAsync(string username, string aplikasi, string role)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);

                var parameters = new
                {
                    Username = username,
                    Aplikasi = aplikasi,
                    Role = role
                };
                var result = await conn.QueryAsync<dynamic>("ATH_TRX_GetMenuByUsername", parameters, commandType: CommandType.StoredProcedure);

                var list = result.Select(row => new Menu
                {
                    Id = (int)row.men_id,
                    ParentId = (int)row.men_parent_id,
                    Icon = (string)row.men_icon,
                    Label = (string)row.men_nama,
                    Href = (string)row.men_link
                }).ToList();

                if (list.Count == 0) return (true, list, null);

                var lookup = list.ToLookup(p => p.ParentId);
                foreach (var menu in list)
                {
                    menu.Children = [.. lookup[menu.Id]];
                }

                return (true, lookup[0].ToList(), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan daftar menu. | [{Username}]", username);
                return (false, [], "Gagal mendapatkan daftar menu.");
            }
        }

        public async Task<(bool IsSuccess, List<string> ListPermission, string? ErrorMessage)> GetPermissionAsync(string username, string aplikasi, string role)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);

                var parameters = new
                {
                    Username = username,
                    Aplikasi = aplikasi,
                    Role = role
                };
                var result = await conn.QueryAsync<string>("ATH_TRX_GetListAkses", parameters, commandType: CommandType.StoredProcedure);

                return (true, result.ToList(), null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan daftar hak akses. | [{Username}]", username);
                return (false, [], "Gagal mendapatkan daftar hak akses.");
            }
        }

        public async Task<bool> HasPermissionAsync(string username, string aplikasi, string role, string permission)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);

                var parameters = new
                {
                    Username = username,
                    Aplikasi = aplikasi,
                    Role = role,
                    Permission = permission
                };
                var result = await conn.QueryFirstOrDefaultAsync<dynamic>("ATH_TRX_GetAksesByUsername", parameters, commandType: CommandType.StoredProcedure);

                return result != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan hak akses. | [{Username}]", username);
                return false;
            }
        }

        public async Task<bool> UpdateProfilePhotoAsync(string username, string photoBase64)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                var rows = await conn.ExecuteScalarAsync<int>(
                    "IOT_SP_User_UpdatePhoto",
                    new { Username = username, Photo = photoBase64 },
                    commandType: CommandType.StoredProcedure);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengupdate foto profil untuk user: {Username}", username);
                return false;
            }
        }

        public async Task<string?> GetProfilePhotoAsync(string username)
        {
            try
            {
                await using var conn = new SqlConnection(_conn);
                return await conn.QueryFirstOrDefaultAsync<string>(
                    "IOT_SP_User_GetPhoto",
                    new { Username = username },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil foto profil untuk user: {Username}", username);
                return null;
            }
        }
    }
}
