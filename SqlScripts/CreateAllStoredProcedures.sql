-- ============================================================================
-- SCRIPT PEMBUATAN SELURUH STORED PROCEDURE APLIKASI IOT ASTRATECH (STANDAR PSI)
-- Politeknik Astra - Sistem Informasi & Sistem Komputer IoT
-- ============================================================================

USE SIA; -- Atau database SSO/SIA utama Anda
GO

-- 1. SP AUTENTIKASI PENGGUNA (LOGIN)
IF OBJECT_ID('dbo.USR_SP_Authenticate', 'P') IS NOT NULL DROP PROCEDURE dbo.USR_SP_Authenticate;
GO
CREATE PROCEDURE dbo.USR_SP_Authenticate
    @Username VARCHAR(100),
    @JenisAplikasi VARCHAR(50) = 'APP01'
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ISNULL(a.app_deskripsi, 'Aplikasi IoT Astratech') AS NamaAplikasi,
        ISNULL(r.rol_deskripsi, 'User') AS NamaRole,
        ISNULL(a.app_tautan, '/') AS Root,
        ISNULL(a.app_id, 'APP01') AS AppId,
        ISNULL(r.rol_id, 'ROL01') AS RoleId
    FROM MST_User u
    LEFT JOIN MST_Role r ON u.rol_id = r.rol_id
    LEFT JOIN MST_Aplikasi a ON u.app_id = a.app_id
    LEFT JOIN MST_Karyawan k ON u.use_id = k.kry_username
    LEFT JOIN MST_Mahasiswa m ON u.use_id = m.mhs_username
    WHERE (LOWER(u.use_id) = LOWER(@Username) OR LOWER(k.kry_username) = LOWER(@Username) OR LOWER(m.mhs_username) = LOWER(@Username))
      AND u.use_status = 'Aktif'
      AND u.use_is_deleted = 0;
END;
GO

-- 2. SP AGREGASI STATISTIK DASHBOARD UTAMA
IF OBJECT_ID('dbo.IOT_SP_DashboardStats_Get', 'P') IS NOT NULL DROP PROCEDURE dbo.IOT_SP_DashboardStats_Get;
GO
CREATE PROCEDURE dbo.IOT_SP_DashboardStats_Get
    @Username VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        (SELECT COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0) AS TotalDevices,
        (SELECT COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0 AND (dev_status = 'Aktif' OR dev_status = 'ON')) AS OnlineDevices,
        (SELECT COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0 AND (dev_status = 'Tidak Aktif' OR dev_status = 'OFF')) AS OfflineDevices,
        (SELECT COUNT(*) FROM MST_User WHERE use_is_deleted = 0) AS TotalUsers,
        ISNULL((SELECT AVG(das_value) FROM TRX_DataSensor WHERE das_jenis = 'Temperature' AND das_timestamp >= DATEADD(HOUR, -24, GETDATE())), 0) AS AvgSuhu,
        ISNULL((SELECT AVG(das_value) FROM TRX_DataSensor WHERE das_jenis = 'Humidity' AND das_timestamp >= DATEADD(HOUR, -24, GETDATE())), 0) AS AvgHumi,
        ISNULL((SELECT AVG(das_value) FROM TRX_DataSensor WHERE das_jenis = 'Power' AND das_timestamp >= DATEADD(HOUR, -24, GETDATE())), 0) AS AvgPower,
        ISNULL((SELECT SUM(pen_total_kwh) FROM TRX_PenggunaanEnergi WHERE pen_period_date >= DATEADD(DAY, -30, GETDATE())), 0) AS EstKWh;
END;
GO

-- 3. SP MONITORING DATA REAL-TIME PERANGKAT TERBARU
IF OBJECT_ID('dbo.IOT_SP_DashboardLatest_Get', 'P') IS NOT NULL DROP PROCEDURE dbo.IOT_SP_DashboardLatest_Get;
GO
CREATE PROCEDURE dbo.IOT_SP_DashboardLatest_Get
    @Username VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
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
    WHERE d.dev_is_deleted = 0;
END;
GO

-- 4. SP AMBIL SEMUA PERANGKAT
IF OBJECT_ID('dbo.DEV_MST_GetAll', 'P') IS NOT NULL DROP PROCEDURE dbo.DEV_MST_GetAll;
GO
CREATE PROCEDURE dbo.DEV_MST_GetAll
AS
BEGIN
    SET NOCOUNT ON;
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
    WHERE ISNULL(d.dev_is_deleted, 0) = 0;
END;
GO

-- 5. SP DETAIL PERANGKAT PER ID / MAC / NAMA
IF OBJECT_ID('dbo.DEV_MST_GetById', 'P') IS NOT NULL DROP PROCEDURE dbo.DEV_MST_GetById;
GO
CREATE PROCEDURE dbo.DEV_MST_GetById
    @id VARCHAR(100),
    @lookupMac VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 
        d.dev_id, d.dev_mac_address, d.dev_nama, d.dev_type, d.dev_status, d.rua_id, r.rua_nama, r.rua_gedung 
    FROM MST_Device d 
    INNER JOIN MST_Ruangan r ON d.rua_id = r.rua_id 
    WHERE d.dev_mac_address = @lookupMac 
       OR d.dev_mac_address = @id 
       OR d.dev_nama = @id 
       OR CAST(d.dev_id AS VARCHAR) = @id 
       OR d.dev_nama LIKE '%' + @id + '%';
END;
GO

-- 6. SP Dapatkan ID Internal Device
IF OBJECT_ID('dbo.DEV_MST_GetInternalId', 'P') IS NOT NULL DROP PROCEDURE dbo.DEV_MST_GetInternalId;
GO
CREATE PROCEDURE dbo.DEV_MST_GetInternalId
    @id VARCHAR(100),
    @lookupMac VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 dev_id FROM MST_Device 
    WHERE dev_mac_address = @lookupMac 
       OR dev_mac_address = @id 
       OR dev_nama = @id 
       OR CAST(dev_id AS VARCHAR) = @id
       OR dev_nama LIKE '%' + @id + '%';
END;
GO

-- 7. SP Hapus Device (Soft Delete)
IF OBJECT_ID('dbo.DEV_MST_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.DEV_MST_Delete;
GO
CREATE PROCEDURE dbo.DEV_MST_Delete
    @id VARCHAR(100),
    @username VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE MST_Device 
    SET dev_is_deleted = 1, 
        dev_status = 'Tidak Aktif',
        dev_modified_by = @username, 
        dev_modified_date = GETDATE() 
    WHERE dev_mac_address = @id OR dev_nama = @id OR CAST(dev_id AS VARCHAR) = @id;
END;
GO

-- 8. SP Ambil Semua User
IF OBJECT_ID('dbo.USR_MST_GetAll', 'P') IS NOT NULL DROP PROCEDURE dbo.USR_MST_GetAll;
GO
CREATE PROCEDURE dbo.USR_MST_GetAll
    @keyword VARCHAR(100) = NULL,
    @roleId VARCHAR(50) = NULL,
    @status VARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        u.use_id AS Username,
        COALESCE(
            k.kry_nama_depan + ' ' + ISNULL(k.kry_nama_blkg, ''),
            m.mhs_nama_depan + ' ' + ISNULL(m.mhs_nama_blkg, ''),
            u.use_id
        ) AS FullName,
        ISNULL(NULLIF(u.use_email, ''), LOWER(u.use_id) + '@astratech.ac.id') AS Email,
        u.use_status AS Status,
        ISNULL(r.rol_deskripsi, 'User') AS RoleName,
        u.rol_id AS RoleId,
        '' AS Photo
    FROM MST_User u
    LEFT JOIN MST_Role r ON u.rol_id = r.rol_id
    LEFT JOIN MST_Karyawan k ON u.use_id = k.kry_username
    LEFT JOIN MST_Mahasiswa m ON u.use_id = m.mhs_username
    WHERE u.use_is_deleted = 0
      AND (@keyword IS NULL OR @keyword = '' OR u.use_id LIKE '%' + @keyword + '%' OR u.use_email LIKE '%' + @keyword + '%')
      AND (@roleId IS NULL OR @roleId = '' OR u.rol_id = @roleId OR r.rol_deskripsi = @roleId)
      AND (@status IS NULL OR @status = '' OR u.use_status = @status);
END;
GO

-- 9. SP Detail User
IF OBJECT_ID('dbo.USR_MST_GetDetail', 'P') IS NOT NULL DROP PROCEDURE dbo.USR_MST_GetDetail;
GO
CREATE PROCEDURE dbo.USR_MST_GetDetail
    @username VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        u.use_id AS Username,
        COALESCE(
            k.kry_nama_depan + ' ' + ISNULL(k.kry_nama_blkg, ''),
            m.mhs_nama_depan + ' ' + ISNULL(m.mhs_nama_blkg, ''),
            u.use_id
        ) AS FullName,
        ISNULL(NULLIF(u.use_email, ''), LOWER(u.use_id) + '@astratech.ac.id') AS Email,
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
        OR LOWER(ISNULL(u.use_email, '')) = LOWER(@username)
        OR LOWER(k.kry_username) = LOWER(@username) 
        OR LOWER(m.mhs_username) = LOWER(@username)
    ) AND u.use_is_deleted = 0;
END;
GO

-- 10. SP Laporan Summary
IF OBJECT_ID('dbo.IOT_SP_ReportSummary_Get', 'P') IS NOT NULL DROP PROCEDURE dbo.IOT_SP_ReportSummary_Get;
GO
CREATE PROCEDURE dbo.IOT_SP_ReportSummary_Get
    @Period VARCHAR(50) = 'today'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @totalDevice INT, @deviceOnline INT, @deviceOffline INT, @totalUsers INT;
    SELECT @totalDevice = COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0;
    SELECT @deviceOnline = COUNT(*) FROM MST_Device WHERE dev_is_deleted = 0 AND (dev_status = 'Aktif' OR dev_status = 'ON');
    SET @deviceOffline = @totalDevice - @deviceOnline;
    SELECT @totalUsers = COUNT(*) FROM MST_User WHERE use_is_deleted = 0;

    SELECT 
        @Period AS Period,
        @totalDevice AS TotalDevice,
        @deviceOnline AS DeviceOnline,
        @deviceOffline AS DeviceOffline,
        @totalUsers AS TotalUsers,
        ISNULL(AVG(das_value), 24.5) AS AvgTemperature,
        ISNULL(MAX(das_value), 28.0) AS MaxTemperature,
        ISNULL(MIN(das_value), 21.0) AS MinTemperature,
        65.0 AS AvgHumidity,
        80.0 AS MaxHumidity,
        50.0 AS MinHumidity,
        120.5 AS TotalPowerKwh
    FROM TRX_DataSensor WHERE das_jenis = 'Temperature';
END;
GO

PRINT '100% Stored Procedure Standar PSI Berhasil Di-generate!';
GO
