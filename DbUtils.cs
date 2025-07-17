using Microsoft.Data.Sqlite;
using System.Data;

namespace FtpSync
{
    public interface IDbUtils
    {
        Task<DbUtils.FileSyncStatus> GetFileSyncStatus(int syncId, string filename, long fileSize, DateTime lastModDate);
        Task<string?> GetFtpPassword();
        Task<string?> GetFtpUrl();
        Task<string?> GetFtpUser();
        public Task InitDb();
        Task MarkFileAsSynhronized(int syncId, string filename, long fileSize, DateTime lastModDate, bool success, string? errors);
        Task SetFtpSettings(string entropy, string url, string user, string pass);
        
        /// <summary>
        /// Returns the sync ID for the given folder name and target folder name.
        /// If the sync does not exist, it will create a new entry and return the new ID.
        /// </summary>
        /// <param name="folderName"></param>
        /// <param name="targetFolderName"></param>
        /// <returns></returns>
        Task<int> GetSyncId(string folderName, string targetFolderName);
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
            comm.CommandText = "CREATE TABLE processedfiles (" +
                               "    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT," +
                               "    filename TEXT NOT NULL," +
                               "    date TEXT NOT NULL," +
                               "    processed INTEGER NOT NULL," +
                               "    errors TEXT NULL," +
                               "    retries INTEGER NOT NULL," +
                               "    filesize INTEGER NOT NULL," +
                               "    lastmoddate TEXT NOT NULL," +
                               "    syncid INTEGER," +
                               "    FOREIGN KEY(syncid) REFERENCES syncs(id));";
            await comm.ExecuteNonQueryAsync();

            comm.CommandText = "CREATE TABLE settings (setname TEXT NOT NULL, setvalue TEXT NOT NULL, PRIMARY KEY (setname));";
            await comm.ExecuteNonQueryAsync();

            comm.CommandText = "CREATE TABLE syncs (id INTEGER PRIMARY KEY AUTOINCREMENT, foldername TEXT NOT NULL, targetfoldername TEXT NOT NULL, date TEXT NOT NULL);";
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

        public async Task<int> GetSyncId(string folderName, string targetFolderName)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            await using var comm = conn.CreateCommand();
            comm.CommandType = System.Data.CommandType.Text;
            comm.CommandText = "SELECT id FROM syncs WHERE foldername = @foldername AND targetfoldername = @targetfoldername";
            comm.Parameters.AddWithValue("foldername", folderName);
            comm.Parameters.AddWithValue("targetfoldername", targetFolderName);

            var res =await comm.ExecuteScalarAsync();
            if (res == null)
            {
                //Non esiste ancora la sync, la creo.
                comm.CommandText = "INSERT INTO syncs (foldername, targetfoldername, date) VALUES (@foldername, @targetfoldername, @date); SELECT last_insert_rowid();";
                comm.Parameters.AddWithValue("date", DateTime.Now.ToString("o"));
                res = await comm.ExecuteScalarAsync();
                if (res == null)
                    throw new Exception("Unable to create sync entry in database.");
            }

            return Convert.ToInt32(res);
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
        public async Task<FileSyncStatus> GetFileSyncStatus(int syncId, string filename, long fileSize, DateTime lastModDate)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            using var comm = conn.CreateCommand();
            comm.CommandType = System.Data.CommandType.Text;
            comm.CommandText = "SELECT * FROM processedfiles WHERE syncid = @syncid AND filename = @filename";
            comm.Parameters.AddWithValue("filename", filename);
            comm.Parameters.AddWithValue("syncid", syncId);
            using var reader = await comm.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return new FileSyncStatus();
            }

            var alreadySynch = reader.GetInt32("processed");
            var fileSizeDb = reader.GetInt64("filesize");
            var lastModDateDb = reader.GetString("lastmoddate");
            var lastModDateDbParsed = DateTime.Parse(lastModDateDb);

            return new FileSyncStatus
            {
                FileExists = true,
                Processed = alreadySynch == 1,
                ProcessedFileId = reader.GetInt32("id"),
                DifferentFileSize = fileSize != fileSizeDb,
                DifferentLastModDate = lastModDate != lastModDateDbParsed.ToUniversalTime(),
                Retries = reader.GetInt32("retries")
            };
        }

        public class FileSyncStatus
        {
            public bool FileExists { get; set; }

            public bool Processed { get; set; } = false;
            public int ProcessedFileId { get; set; } = 0;
            public bool DifferentFileSize { get; set; } = false;
            public bool DifferentLastModDate { get; set; } = false;
            public int Retries { get; set; } = 0;
        }


        /// <summary>
        /// Marca il file come sincronizzato.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public async Task MarkFileAsSynhronized(int syncId, string filename, long fileSize, DateTime lastModDate, bool success, string? errors)
        {
            var stat = await GetFileSyncStatus(syncId, filename, fileSize, lastModDate);
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var comm = conn.CreateCommand();
            comm.CommandType = CommandType.Text;
            if (!stat.FileExists)
            {
                //insert
                comm.CommandText = "INSERT INTO processedfiles (filename, date, processed, errors, retries, filesize, lastmoddate, syncid) VALUES (@filename, @date, @processed, @errors, @retries, @filesize, @lastmoddate, @syncid)";
                comm.Parameters.AddWithValue("filename", filename);
                comm.Parameters.AddWithValue("date", DateTime.Now.ToString("o"));
                comm.Parameters.AddWithValue("processed", success ? 1 : 0);
                comm.Parameters.AddWithValue("filesize", fileSize);
                comm.Parameters.AddWithValue("lastmoddate", lastModDate.ToString("o"));
                comm.Parameters.AddWithValue("syncid", syncId);
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
                comm.Parameters.AddWithValue("retries", stat.Retries + 1);
            }

            await comm.ExecuteNonQueryAsync();
        }
    }
}