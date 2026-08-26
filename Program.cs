using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Sharp7;
using Microsoft.Data.Sqlite;
using Npgsql;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace plc_data_reader_cross_app
{
    class Program
    {
        private static string localDbConnectionString = "Data Source=plc_data.db";
        private static string cloudConnectionString = "Host=db.uzhysodwllhgoyoytyed.supabase.co; Port=5432; Database=postgres; Username=postgres; Password=153579Abcsupabase";
        
        private static string AdminPassword = "plcdatareaderadmin";

        public static MainWindow? MainWindowInstance { get; set; }

        [STAThread]
        static void Main(string[] args)
        {
            Task.Run(() => StartPlcLoopAsync());
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();

        private static async Task StartPlcLoopAsync()
        {
            InitializeLocalDatabase();
            SeedDefaultDeviceIfEmpty();

            await PullDataFromCloudAsync();
            _ = StartCloudSyncWorkerAsync();

            while (true)
            {
                var devices = GetActivePlcDevices();

                if (devices.Count == 0)
                {
                    MainWindowInstance?.UpdateUI(0, "⚠️ Tanımlı PLC/Cihaz Bulunamadı");
                    await Task.Delay(3000);
                    continue;
                }

                foreach (var dev in devices)
                {
                    int processValue = 0;
                    string sourceType = "BAĞLANTI BEKLENİYOR";

                    S7Client client = new S7Client();
                    bool isPlcConnectedSuccessfully = false;

                    try
                    {
                        int connectResult = client.ConnectTo(dev.IpAddress, dev.Rack, dev.Slot);
                        if (connectResult == 0)
                        {
                            isPlcConnectedSuccessfully = true;
                            int startByte = 0;  
                            int size = 70;      
                            byte[] buffer = new byte[size];

                            int readResult = client.ReadArea(0x84, dev.DbNumber, startByte, size, 0x02, buffer);

                            if (readResult == 0)
                            {
                                if (buffer.Length >= 50)
                                {
                                    byte[] valBytes = new byte[4];
                                    Array.Copy(buffer, 46, valBytes, 0, 4);
                                    Array.Reverse(valBytes);
                                    processValue = BitConverter.ToInt32(valBytes, 0);
                                }

                                sourceType = $"🟢 {dev.PlcName} (DB{dev.DbNumber} - Okuma Başarılı)";
                            }
                            else
                            {
                                sourceType = $"🔴 {dev.PlcName} OKUMA HATASI (Kod: {readResult})";
                            }
                        }
                        else
                        {
                            sourceType = $"☁️ Bulut Modu (PLC Erişilemiyor, Veriler Supabase'den Okunuyor)";
                        }
                    }
                    catch (Exception ex)
                    {
                        sourceType = $"☁️ Bulut Modu (İstisna: {ex.Message})";
                    }
                    finally
                    {
                        if (client.Connected)
                        {
                            try { client.Disconnect(); } catch { }
                        }
                    }

                    if (isPlcConnectedSuccessfully)
                    {
                        SaveDataToLocalDatabase(dev.PlcName, dev.IpAddress, dev.DbNumber, processValue, sourceType);
                        MainWindowInstance?.UpdateUI(processValue, sourceType);
                        CheckAndTriggerAlarms(dev.Id, dev.PlcName, processValue);
                    }
                    else
                    {
                        await PullDataFromCloudAsync();
                        MainWindowInstance?.UpdateUI(0, sourceType);
                    }

                    await Task.Delay(1000);
                }

                await Task.Delay(2000);
            }
        }

        private static void InitializeLocalDatabase()
        {
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                
                string createDevicesTable = @"
                    CREATE TABLE IF NOT EXISTS plc_devices (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        plc_name TEXT NOT NULL,
                        plc_ip TEXT NOT NULL,
                        rack INTEGER DEFAULT 0,
                        slot INTEGER DEFAULT 1,
                        db_number INTEGER NOT NULL,
                        is_active INTEGER DEFAULT 1
                    );";

                string createLogsTable = @"
                    CREATE TABLE IF NOT EXISTS plc_logs (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        log_time TEXT NOT NULL,
                        plc_name TEXT NOT NULL,
                        plc_ip TEXT NOT NULL,
                        db_number INTEGER NOT NULL,
                        process_value INTEGER NOT NULL,
                        source_type TEXT NOT NULL,
                        is_synced INTEGER DEFAULT 0
                    );";

                string createAlarmSettingsTable = @"
                    CREATE TABLE IF NOT EXISTS alarm_settings (
                        device_id INTEGER PRIMARY KEY,
                        high_high REAL DEFAULT 90,
                        high REAL DEFAULT 75,
                        low_low REAL DEFAULT 10
                    );";

                string createUsersTable = @"
                    CREATE TABLE IF NOT EXISTS users (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        username TEXT UNIQUE NOT NULL,
                        password TEXT NOT NULL,
                        role TEXT NOT NULL
                    );";

                string createActivityLogsTable = @"
                    CREATE TABLE IF NOT EXISTS activity_logs (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        username TEXT NOT NULL,
                        action_type TEXT NOT NULL,
                        details TEXT NOT NULL,
                        action_time TEXT NOT NULL
                    );";

                using var cmd1 = new SqliteCommand(createDevicesTable, conn);
                cmd1.ExecuteNonQuery();

                using var cmd2 = new SqliteCommand(createLogsTable, conn);
                cmd2.ExecuteNonQuery();

                using var cmd3 = new SqliteCommand(createAlarmSettingsTable, conn);
                cmd3.ExecuteNonQuery();

                using var cmd4 = new SqliteCommand(createUsersTable, conn);
                cmd4.ExecuteNonQuery();

                using var cmd5 = new SqliteCommand(createActivityLogsTable, conn);
                cmd5.ExecuteNonQuery();

                string checkUser = "SELECT COUNT(*) FROM users";
                using var checkCmd = new SqliteCommand(checkUser, conn);
                long userCount = (long)checkCmd.ExecuteScalar()!;
                if (userCount == 0)
                {
                    string insertAdmin = "INSERT INTO users (username, password, role) VALUES ('admin', '123456', 'Admin')";
                    using var adminCmd = new SqliteCommand(insertAdmin, conn);
                    adminCmd.ExecuteNonQuery();
                }

                try { using var alter1 = new SqliteCommand("ALTER TABLE plc_logs ADD COLUMN plc_name TEXT DEFAULT 'PLC-1';", conn); alter1.ExecuteNonQuery(); } catch { }
                try { using var alter2 = new SqliteCommand("ALTER TABLE plc_logs ADD COLUMN plc_ip TEXT DEFAULT '192.168.123.150';", conn); alter2.ExecuteNonQuery(); } catch { }
                try { using var alter3 = new SqliteCommand("ALTER TABLE plc_logs ADD COLUMN db_number INTEGER DEFAULT 100;", conn); alter3.ExecuteNonQuery(); } catch { }
            }
            catch { }
        }

        public static void LogUserActivity(string username, string actionType, string details)
        {
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string query = "INSERT INTO activity_logs (username, action_type, details, action_time) VALUES (@user, @action, @details, @time)";
                using var cmd = new SqliteCommand(query, conn);
                cmd.Parameters.AddWithValue("@user", username);
                cmd.Parameters.AddWithValue("@action", actionType);
                cmd.Parameters.AddWithValue("@details", details);
                cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static List<ActivityModel> GetAllActivities()
        {
            var list = new List<ActivityModel>();
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string query = "SELECT username, action_type, details, action_time FROM activity_logs ORDER BY id DESC LIMIT 100";
                using var cmd = new SqliteCommand(query, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ActivityModel
                    {
                        Username = reader.GetString(0),
                        ActionType = reader.GetString(1),
                        Details = reader.GetString(2),
                        ActionTime = reader.GetString(3)
                    });
                }
            }
            catch { }
            return list;
        }

        public static List<UserModel> GetAllUsers()
        {
            var list = new List<UserModel>();
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string query = "SELECT id, username, role FROM users";
                using var cmd = new SqliteCommand(query, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new UserModel
                    {
                        Id = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        Role = reader.GetString(2)
                    });
                }
            }
            catch { }
            return list;
        }

        public static bool UpdateUserPassword(string adminPass, int userId, string newPassword)
        {
            if (!VerifyAdminPassword(adminPass)) return false;
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string query = "UPDATE users SET password = @pass WHERE id = @id";
                using var cmd = new SqliteCommand(query, conn);
                cmd.Parameters.AddWithValue("@pass", newPassword);
                cmd.Parameters.AddWithValue("@id", userId);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch { return false; }
        }

        private static void SeedDefaultDeviceIfEmpty()
        {
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string checkQuery = "SELECT COUNT(*) FROM plc_devices";
                using var checkCmd = new SqliteCommand(checkQuery, conn);
                long count = (long)checkCmd.ExecuteScalar()!;

                if (count == 0)
                {
                    string insertDefault = @"INSERT INTO plc_devices (plc_name, plc_ip, rack, slot, db_number, is_active) 
                                             VALUES ('PLC-1 Ana Tesis', '192.168.123.150', 0, 1, 100, 1)";
                    using var insertCmd = new SqliteCommand(insertDefault, conn);
                    insertCmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        public static bool VerifyAdminPassword(string inputPassword)
        {
            return inputPassword == AdminPassword;
        }

        public static (bool success, string role) AuthenticateUser(string username, string password)
        {
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string query = "SELECT role FROM users WHERE username = @user AND password = @pass";
                using var cmd = new SqliteCommand(query, conn);
                cmd.Parameters.AddWithValue("@user", username);
                cmd.Parameters.AddWithValue("@pass", password);
                
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return (true, reader.GetString(0));
                }
            }
            catch { }
            return (false, "");
        }

        public static bool RegisterUser(string adminPass, string newUsername, string newPassword, string role)
        {
            if (!VerifyAdminPassword(adminPass)) return false;

            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string query = "INSERT INTO users (username, password, role) VALUES (@user, @pass, @role)";
                using var cmd = new SqliteCommand(query, conn);
                cmd.Parameters.AddWithValue("@user", newUsername);
                cmd.Parameters.AddWithValue("@pass", newPassword);
                cmd.Parameters.AddWithValue("@role", role);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch { return false; }
        }

        public static bool AddPlcDeviceSecure(string adminPassword, string plcName, string ipAddress, int rack, int slot, int dbNumber)
        {
            if (!VerifyAdminPassword(adminPassword)) return false;

            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string insertQuery = @"INSERT INTO plc_devices (plc_name, plc_ip, rack, slot, db_number, is_active) 
                                       VALUES (@name, @ip, @rack, @slot, @db, 1)";
                using var cmd = new SqliteCommand(insertQuery, conn);
                cmd.Parameters.AddWithValue("@name", plcName);
                cmd.Parameters.AddWithValue("@ip", ipAddress);
                cmd.Parameters.AddWithValue("@rack", rack);
                cmd.Parameters.AddWithValue("@slot", slot);
                cmd.Parameters.AddWithValue("@db", dbNumber);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch { return false; }
        }

        public static bool DeletePlcDeviceSecure(string adminPassword, int deviceId)
        {
            if (!VerifyAdminPassword(adminPassword)) return false;

            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string deleteQuery = "DELETE FROM plc_devices WHERE id = @id";
                using var cmd = new SqliteCommand(deleteQuery, conn);
                cmd.Parameters.AddWithValue("@id", deviceId);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch { return false; }
        }

        public static bool SaveAlarmThresholdsSecure(string adminPassword, int deviceId, double highHigh, double high, double lowLow)
        {
            if (!VerifyAdminPassword(adminPassword)) return false;

            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string upsertQuery = @"
                    INSERT INTO alarm_settings (device_id, high_high, high, low_low) 
                    VALUES (@devId, @hh, @h, @ll)
                    ON CONFLICT(device_id) DO UPDATE SET 
                        high_high = @hh, 
                        high = @h, 
                        low_low = @ll;";
                using var cmd = new SqliteCommand(upsertQuery, conn);
                cmd.Parameters.AddWithValue("@devId", deviceId);
                cmd.Parameters.AddWithValue("@hh", highHigh);
                cmd.Parameters.AddWithValue("@h", high);
                cmd.Parameters.AddWithValue("@ll", lowLow);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch { return false; }
        }

        private static void CheckAndTriggerAlarms(int deviceId, string plcName, int val)
        {
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string query = "SELECT high_high, high, low_low FROM alarm_settings WHERE device_id = @id";
                using var cmd = new SqliteCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", deviceId);
                using var reader = cmd.ExecuteReader();

                double hh = 90, h = 75, ll = 10;
                if (reader.Read())
                {
                    hh = reader.GetDouble(0);
                    h = reader.GetDouble(1);
                    ll = reader.GetDouble(2);
                }

                string timeStr = DateTime.Now.ToString("HH:mm:ss");
                if (val >= hh)
                {
                    MainWindowInstance?.AppendAlarmLog($"[{timeStr}] 🚨 KRİTİK YÜKSEK (HH): {plcName} Değer={val} (Limit: {hh})");
                }
                else if (val >= h)
                {
                    MainWindowInstance?.AppendAlarmLog($"[{timeStr}] ⚠️ YÜKSEK UYARI (H): {plcName} Değer={val} (Limit: {h})");
                }
                else if (val <= ll)
                {
                    MainWindowInstance?.AppendAlarmLog($"[{timeStr}] ⚠️ KRİTİK DÜŞÜK (LL): {plcName} Değer={val} (Limit: {ll})");
                }
            }
            catch { }
        }

        public static List<PlcDeviceModel> GetActivePlcDevices()
        {
            var list = new List<PlcDeviceModel>();
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string query = "SELECT id, plc_name, plc_ip, rack, slot, db_number FROM plc_devices WHERE is_active = 1";
                using var cmd = new SqliteCommand(query, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new PlcDeviceModel
                    {
                        Id = reader.GetInt32(0),
                        PlcName = reader.GetString(1),
                        IpAddress = reader.GetString(2),
                        Rack = reader.GetInt32(3),
                        Slot = reader.GetInt32(4),
                        DbNumber = reader.GetInt32(5)
                    });
                }
            }
            catch { }
            return list;
        }

        private static void SaveDataToLocalDatabase(string plcName, string plcIp, int dbNumber, int value, string source)
        {
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();
                string insertQuery = @"INSERT INTO plc_logs (log_time, plc_name, plc_ip, db_number, process_value, source_type, is_synced) 
                                       VALUES (@time, @name, @ip, @db, @val, @source, 0)";
                using var cmd = new SqliteCommand(insertQuery, conn);
                cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@name", plcName);
                cmd.Parameters.AddWithValue("@ip", plcIp);
                cmd.Parameters.AddWithValue("@db", dbNumber);
                cmd.Parameters.AddWithValue("@val", value);
                cmd.Parameters.AddWithValue("@source", source);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static List<LogRecordModel> GetFilteredLogs(int? year, int? month, int? day, string startTime, string endTime)
        {
            var logs = new List<LogRecordModel>();
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();

                string query = "SELECT id, log_time, plc_name, db_number, process_value, source_type FROM plc_logs ORDER BY id DESC;";

                using var cmd = new SqliteCommand(query, conn);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    try
                    {
                        var logTime = DateTime.Parse(reader.GetString(1));
                        
                        if (year.HasValue && logTime.Year != year.Value) continue;
                        if (month.HasValue && logTime.Month != month.Value) continue;
                        if (day.HasValue && logTime.Day != day.Value) continue;

                        logs.Add(new LogRecordModel
                        {
                            Id = reader.GetInt32(0),
                            LogTime = logTime,
                            PlcName = reader.GetString(2),
                            DbNumber = reader.GetInt32(3),
                            ProcessValue = reader.GetInt32(4),
                            SourceType = reader.GetString(5)
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return logs;
        }

        public static List<LogRecordModel> GetLogsInRange(DateTime start, DateTime end)
        {
            var logs = new List<LogRecordModel>();
            try
            {
                using var conn = new SqliteConnection(localDbConnectionString);
                conn.Open();

                string query = "SELECT id, log_time, plc_name, db_number, process_value, source_type FROM plc_logs ORDER BY log_time ASC;";

                using var cmd = new SqliteCommand(query, conn);
                using var reader = cmd.ExecuteReader();

                DateTime rangeStart = start.Date;
                DateTime rangeEndExclusive = end.Date.AddDays(1);

                while (reader.Read())
                {
                    try
                    {
                        var logTime = DateTime.Parse(reader.GetString(1));

                        if (logTime < rangeStart || logTime >= rangeEndExclusive) continue;

                        logs.Add(new LogRecordModel
                        {
                            Id = reader.GetInt32(0),
                            LogTime = logTime,
                            PlcName = reader.GetString(2),
                            DbNumber = reader.GetInt32(3),
                            ProcessValue = reader.GetInt32(4),
                            SourceType = reader.GetString(5)
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return logs;
        }

        private static async Task PullDataFromCloudAsync()
        {
            if (!IsInternetAvailable()) return;

            try
            {
                using var cloudConn = new NpgsqlConnection(cloudConnectionString);
                await cloudConn.OpenAsync();

                string selectCloudQuery = "SELECT log_time, plc_name, plc_ip, db_number, process_value, source_type FROM plc_logs ORDER BY log_time DESC LIMIT 500";
                using var cloudCmd = new NpgsqlCommand(selectCloudQuery, cloudConn);
                using var reader = await cloudCmd.ExecuteReaderAsync();

                using var localConn = new SqliteConnection(localDbConnectionString);
                await localConn.OpenAsync();

                while (await reader.ReadAsync())
                {
                    string logTime = reader.GetDateTime(0).ToString("yyyy-MM-dd HH:mm:ss");
                    string plcName = reader.GetString(1);
                    string plcIp = reader.GetString(2);
                    int dbNumber = reader.GetInt32(3);
                    int processValue = reader.GetInt32(4);
                    string sourceType = reader.GetString(5);

                    string checkExist = "SELECT COUNT(1) FROM plc_logs WHERE log_time = @time AND plc_name = @name AND process_value = @val";
                    using var checkCmd = new SqliteCommand(checkExist, localConn);
                    checkCmd.Parameters.AddWithValue("@time", logTime);
                    checkCmd.Parameters.AddWithValue("@name", plcName);
                    checkCmd.Parameters.AddWithValue("@val", processValue);

                    long exists = (long)checkCmd.ExecuteScalar()!;
                    if (exists == 0)
                    {
                        string insertLocal = @"INSERT INTO plc_logs (log_time, plc_name, plc_ip, db_number, process_value, source_type, is_synced) 
                                               VALUES (@time, @name, @ip, @db, @val, @source, 1)";
                        using var insertCmd = new SqliteCommand(insertLocal, localConn);
                        insertCmd.Parameters.AddWithValue("@time", logTime);
                        insertCmd.Parameters.AddWithValue("@name", plcName);
                        insertCmd.Parameters.AddWithValue("@ip", plcIp);
                        insertCmd.Parameters.AddWithValue("@db", dbNumber);
                        insertCmd.Parameters.AddWithValue("@val", processValue);
                        insertCmd.Parameters.AddWithValue("@source", sourceType);
                        insertCmd.ExecuteNonQuery();
                    }
                }
                MainWindowInstance?.UpdateCloudSyncStatus("☁️ Supabase'den Geçmiş Veriler Çekildi (Pull Aktif)");
            }
            catch (Exception ex)
            {
                MainWindowInstance?.UpdateCloudSyncStatus($"⚠️ Buluttan Veri Çekme Hatası: {ex.Message}");
            }
        }

        private static async Task StartCloudSyncWorkerAsync()
        {
            while (true)
            {
                await Task.Delay(15000);
                if (IsInternetAvailable())
                {
                    try
                    {
                        using var localConn = new SqliteConnection(localDbConnectionString);
                        localConn.Open();
                        string selectQuery = "SELECT id, log_time, plc_name, plc_ip, db_number, process_value, source_type FROM plc_logs WHERE is_synced = 0 LIMIT 20";
                        using var selectCmd = new SqliteCommand(selectQuery, localConn);
                        using var reader = selectCmd.ExecuteReader();

                        var unsyncedRows = new List<(int id, string time, string name, string ip, int db, int val, string source)>();
                        while (reader.Read())
                        {
                            unsyncedRows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetString(6)));
                        }
                        reader.Close();

                        if (unsyncedRows.Count > 0)
                        {
                            using var cloudConn = new NpgsqlConnection(cloudConnectionString);
                            cloudConn.Open();
                            foreach (var row in unsyncedRows)
                            {
                                string cloudInsert = "INSERT INTO plc_logs (log_time, plc_name, plc_ip, db_number, process_value, source_type) VALUES (@time, @name, @ip, @db, @val, @source)";
                                using var cloudCmd = new NpgsqlCommand(cloudInsert, cloudConn);
                                cloudCmd.Parameters.AddWithValue("@time", DateTime.Parse(row.time));
                                cloudCmd.Parameters.AddWithValue("@name", row.name);
                                cloudCmd.Parameters.AddWithValue("@ip", row.ip);
                                cloudCmd.Parameters.AddWithValue("@db", row.db);
                                cloudCmd.Parameters.AddWithValue("@val", row.val);
                                cloudCmd.Parameters.AddWithValue("@source", row.source);
                                cloudCmd.ExecuteNonQuery();

                                string updateQuery = "UPDATE plc_logs SET is_synced = 1 WHERE id = @id";
                                using var updateCmd = new SqliteCommand(updateQuery, localConn);
                                updateCmd.Parameters.AddWithValue("@id", row.id);
                                updateCmd.ExecuteNonQuery();
                            }

                            MainWindowInstance?.UpdateCloudSyncStatus("☁️ Supabase Bulut Senkronizasyonu Aktif (Veriler Eşitlendi)");
                        }
                    }
                    catch { }
                }
                else
                {
                    MainWindowInstance?.UpdateCloudSyncStatus("⚠️ İnternet Yok - Veriler Sadece Yerel SQLite'da Saklanıyor");
                }
            }
        }

        private static bool IsInternetAvailable()
        {
            try
            {
                using var ping = new Ping();
                var reply = ping.Send("8.8.8.8", 1500);
                return reply != null && reply.Status == IPStatus.Success;
            }
            catch { return false; }
        }
    }

    public class PlcDeviceModel
    {
        public int Id { get; set; }
        public string PlcName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Rack { get; set; }
        public int Slot { get; set; }
        public int DbNumber { get; set; }
    }

    public class LogRecordModel
    {
        public int Id { get; set; }
        public DateTime LogTime { get; set; }
        public string PlcName { get; set; } = string.Empty;
        public int DbNumber { get; set; }
        public int ProcessValue { get; set; }
        public string SourceType { get; set; } = string.Empty;
    }

    public class UserModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class ActivityModel
    {
        public string Username { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string ActionTime { get; set; } = string.Empty;
    }
}