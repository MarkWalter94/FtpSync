using Microsoft.Data.Sqlite;
using System.Data;

namespace FtpSync
{
    public interface IDbUtils
    {
        Task<int> GetFileSyncStatus(string filename, long fileSize, DateTime lastModDate);
        Task<string?> GetFtpPassword();
        Task<string?> GetFtpUrl();
        Task<string?> GetFtpUser();
        public Task InitDb();
        Task MarkFileAsSynhronized(string filename, long fileSize, DateTime lastModDate, bool success, string? errors);
        Task SetFtpSettings(string entropy, string url, string user, string pass);
    }

    public class DbUtils : IDbUtils
    {
        private readonly IConfiguration _configuration;
        private readonly IEncriptionUtils _encriptionUtils;
        private readonly string _dbPath;
        private const string FTPPASS = "pV0FtyJY6UE";
        private const string FTPUSER = "uQ6JPMRFy8B";
        private const string FTPURL = "hfBdrcrbfdH";

        public DbUtils(IConfiguration configuration, IEncriptionUtils encriptionUtils)
        {
            _encriptionUtils = encriptionUtils;
            _configuration = configuration;
            _dbPath = _configuration["Database:Path"]!;
        }

        private SqliteConnection GetConnection()
        {
            return new SqliteConnection($"Data Source={_dbPath}");
        }

        public async Task InitDb()
        {
            if (File.Exists(_dbPath))
                return;

            using var conn = GetConnection();
            await conn.OpenAsync();

            using var comm = conn.CreateCommand();
            comm.CommandType = System.Data.CommandType.Text;
            comm.CommandText = "CREATE TABLE processedfiles (filename TEXT NOT NULL, date TEXT NOT NULL, processed INTEGER NOT NULL, errors TEXT NULL, retries INTEGER NOT NULL, filesize INTEGER NOT NULL, lastmoddate TEXT NOT NULL, PRIMARY KEY (filename));";
            await comm.ExecuteNonQueryAsync();

            comm.CommandText = "CREATE TABLE settings (setname TEXT NOT NULL, setvalue TEXT NOT NULL, PRIMARY KEY (setname));";
            await comm.ExecuteNonQueryAsync();
        }

        public async Task<string?> GetFtpUrl()
        {
            var encUrl = await GetFtpSetting(FTPURL);
            if (string.IsNullOrEmpty(encUrl))
                return null;
            return _encriptionUtils.Decrypt(encUrl);
        }

        public async Task<string?> GetFtpUser()
        {
            var encUser = await GetFtpSetting(FTPUSER);
            if (string.IsNullOrEmpty(encUser))
                return null;
            return _encriptionUtils.Decrypt(encUser);
        }

        public async Task<string?> GetFtpPassword()
        {
            var encPwd = await GetFtpSetting(FTPPASS);
            if (string.IsNullOrEmpty(encPwd))
                return null;
            return _encriptionUtils.Decrypt(encPwd);
        }

        public async Task SetFtpSettings(string entropy, string url, string user, string pass)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            using var comm = conn.CreateCommand();
            comm.CommandType = System.Data.CommandType.Text;

            comm.CommandText = "DELETE FROM settings WHERE setname in (@s1,@s2,@s3)";
            comm.Parameters.AddWithValue("s1", FTPPASS);
            comm.Parameters.AddWithValue("s2", FTPUSER);
            comm.Parameters.AddWithValue("s3", FTPURL);
            await comm.ExecuteNonQueryAsync();

            comm.Parameters.Clear();
            comm.CommandText = "INSERT INTO settings (setname, setvalue) VALUES (@name,@value)";
            comm.Parameters.AddWithValue("name", FTPUSER);
            comm.Parameters.AddWithValue("value", _encriptionUtils.Encrypt(user));
            await comm.ExecuteNonQueryAsync();

            comm.Parameters.Clear();
            comm.CommandText = "INSERT INTO settings (setname, setvalue) VALUES (@name,@value)";
            comm.Parameters.AddWithValue("name", FTPPASS);
            comm.Parameters.AddWithValue("value", _encriptionUtils.Encrypt(pass));
            await comm.ExecuteNonQueryAsync();

            comm.Parameters.Clear();
            comm.CommandText = "INSERT INTO settings (setname, setvalue) VALUES (@name,@value)";
            comm.Parameters.AddWithValue("name", FTPURL);
            comm.Parameters.AddWithValue("value", _encriptionUtils.Encrypt(url));
            await comm.ExecuteNonQueryAsync();
        }

        private async Task<string?> GetFtpSetting(string setting)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            using var comm = conn.CreateCommand();
            comm.CommandType = System.Data.CommandType.Text;
            comm.CommandText = "SELECT * FROM settings WHERE setname = @setname";
            comm.Parameters.AddWithValue("setname", setting);
            using var reader = await comm.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;
            return reader["setvalue"].ToString();
        }

        /// <summary>
        /// Dato un filename ritorna quanti tentativi sono stati fatti per sincronizzarlo:
        /// 0 se non è ancora mai stato sincronizzato.
        /// n > 0 se è già stato sincronizzato n volte con errori.
        /// -1 se è già stato sincronizzato con successo.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public async Task<int> GetFileSyncStatus(string filename, long fileSize, DateTime lastModDate)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            using var comm = conn.CreateCommand();
            comm.CommandType = System.Data.CommandType.Text;
            comm.CommandText = "SELECT * FROM processedfiles WHERE filename = @filename";
            comm.Parameters.AddWithValue("filename", filename);
            using var reader = await comm.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return 0;
            }

            var alreadySynch = reader.GetInt32("processed");
            var fileSizeDb = reader.GetInt64("filesize");
            var lastModDateDb = reader.GetString("lastmoddate");
            var lastModDateDbParsed = DateTime.Parse(lastModDateDb);
            if (fileSizeDb != fileSize)
            {
                return -2;
            }
            else if (lastModDate != lastModDateDbParsed.ToUniversalTime())
            {
                return -3;
            }

            if (alreadySynch == 1)
                return -1;

            return reader.GetInt32("retries");
        }


        /// <summary>
        /// Marca il file come sincronizzato.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public async Task MarkFileAsSynhronized(string filename, long fileSize, DateTime lastModDate, bool success, string? errors)
        {
            var stat = await GetFileSyncStatus(filename, fileSize, lastModDate);
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var comm = conn.CreateCommand();
            comm.CommandType = CommandType.Text;
            if (stat == 0)
            {
                //insert
                comm.CommandText = "INSERT INTO processedfiles (filename, date, processed, errors, retries, filesize, lastmoddate) VALUES (@filename, @date, @processed, @errors, @retries, @filesize, @lastmoddate)";
                comm.Parameters.AddWithValue("filename", filename);
                comm.Parameters.AddWithValue("date", DateTime.Now.ToString("o"));
                comm.Parameters.AddWithValue("processed", success ? 1 : 0);
                comm.Parameters.AddWithValue("filesize", fileSize);
                comm.Parameters.AddWithValue("lastmoddate", lastModDate.ToString("o"));
                if (errors == null)
                    comm.Parameters.AddWithValue("errors", DBNull.Value);
                else
                    comm.Parameters.AddWithValue("errors", errors);
                comm.Parameters.AddWithValue("retries", 1);
            }
            else
            {
                //update
                comm.CommandText = "UPDATE processedfiles SET lastmoddate = @lastmoddate, filesize = @filesize, processed = @processed, retries = @retries, errors = @errors WHERE filename = @filename";
                comm.Parameters.AddWithValue("filename", filename);
                comm.Parameters.AddWithValue("processed", success ? 1 : 0);
                comm.Parameters.AddWithValue("errors", DBNull.Value);
                comm.Parameters.AddWithValue("filesize", fileSize);
                comm.Parameters.AddWithValue("lastmoddate", lastModDate.ToString("o"));
                if (stat > 0)
                {
                    comm.Parameters.AddWithValue("retries", stat + 1);
                }
                else
                {
                    comm.Parameters.AddWithValue("retries", 1);
                }
                
            }

            await comm.ExecuteNonQueryAsync();
        }
    }
}