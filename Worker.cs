using FluentFTP;

namespace FtpSync
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private bool _showProgress = false;
        private long _fileDimensions = 0;

        const long FILE_SIZE_FOR_PROGRESS = 1024 * 1024;

        public Worker(ILogger<Worker> logger, IConfiguration configuration, IHostApplicationLifetime applicationLifetime, IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _applicationLifetime = applicationLifetime;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var initScope = _serviceScopeFactory.CreateScope();
            var dbUtilsInit = initScope.ServiceProvider.GetRequiredService<IDbUtils>();
            await dbUtilsInit.InitDb();

            string server;
            string user;
            string pwd;

            var ftpSettings = initScope.ServiceProvider.GetService<FtpSettings>();
            if (ftpSettings != null)
            {
                //Crypto e metto nel db.

                server = ftpSettings.Server;
                user = ftpSettings.User;
                pwd = ftpSettings.Password;
                var entropy = ftpSettings.Entropy;

                await dbUtilsInit.SetFtpSettings(entropy, server, user, pwd);
            }
            else
            {
                //Carico da db.
                server = await dbUtilsInit.GetFtpUrl();
                user = await dbUtilsInit.GetFtpUser();
                pwd = await dbUtilsInit.GetFtpPassword();
            }

            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogError("Server or user settings not configured! stopping!");
                KillMe();
            }

            var foldersFilesToUpload = _configuration.GetSection("Files").Get<List<SyncFolders>>();
            var maxRetries = _configuration.GetValue<int>("Settings:MaxRetries");
            var deleteAfterTransfer = _configuration.GetValue<bool>("Settings:DeleteAfterTransfer");
            var loopSeconds = _configuration.GetValue<int>("Settings:LoopSeconds");

            if (foldersFilesToUpload == null || !foldersFilesToUpload.Any())
            {
                _logger.LogError($"Indicate at least one folder to sync, stopping!");
                KillMe();
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var dbUtils = scope.ServiceProvider.GetRequiredService<IDbUtils>();
                    //_logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                    using var client = new FtpClient(server, user, pwd);
                    foreach (var folderFilesToUpload in foldersFilesToUpload)
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        var syncId = await dbUtils.GetSyncId(folderFilesToUpload.FilesToUploadFolder, folderFilesToUpload.TargetFolderFtp);

                        foreach (var file in Directory.EnumerateFiles(folderFilesToUpload.FilesToUploadFolder, "*.*", SearchOption.AllDirectories))
                        {
                            if (stoppingToken.IsCancellationRequested)
                                break;
                            var fi = new FileInfo(file);
                            var fileSize = fi.Length;
                            var lastModDate = fi.LastWriteTimeUtc;

                            var fullPathFile = Path.GetFullPath(file);
                            var fileStat = await dbUtils.GetFileSyncStatus(syncId, Path.GetRelativePath(folderFilesToUpload.FilesToUploadFolder, fullPathFile), fileSize, lastModDate);
                            
                            if (fileStat.DifferentFileSize || fileStat.DifferentLastModDate)
                            {
                                _logger.LogDebug($"File {file} modified, replacing..");
                            }
                            else if (fileStat.Retries > maxRetries)
                            {
                                _logger.LogDebug($"File {file} max retries reached, skipping..");
                                continue; //Troppi tentativi.
                            }else if (fileStat.Processed)
                            {
                                //Già sincronizzato.
                                _logger.LogDebug($"File {file} already synched, skipping..");
                                continue;
                            }

                            _logger.LogDebug($"Sync of file {file}...");

                            //Carico il file..
                            try
                            {
                                if (!client.IsConnected)
                                    client.AutoConnect();

                                var result = await Task.Run(() =>
                                {
                                    _fileDimensions = fi.Length;
                                    _showProgress = _fileDimensions >= FILE_SIZE_FOR_PROGRESS;

                                    return client.UploadFile(fullPathFile, Path.Combine(folderFilesToUpload.TargetFolderFtp, Path.GetRelativePath(folderFilesToUpload.FilesToUploadFolder, fullPathFile)), FtpRemoteExists.Overwrite, true, progress: Progress);
                                });
                                _logger.LogDebug("File synched!");
                                await dbUtils.MarkFileAsSynhronized(syncId, Path.GetRelativePath(folderFilesToUpload.FilesToUploadFolder, fullPathFile), fileSize, lastModDate, true, null);
                                if (deleteAfterTransfer)
                                {
                                    try
                                    {
                                        File.Delete(fullPathFile);
                                    }
                                    catch (Exception fsex)
                                    {
                                        _logger.LogError(fsex, $"Error during delete of {fullPathFile}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error during sync of {fullPathFile}");
                                var errstr = $"Error during sync of {fullPathFile}, detail:{Environment.NewLine}{ex}";
                                await dbUtils.MarkFileAsSynhronized(syncId, Path.GetRelativePath(folderFilesToUpload.FilesToUploadFolder, fullPathFile), fileSize, lastModDate, false, errstr);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error during program execution");
                }

                _logger.LogDebug("All done, going to sleep..");
                await Task.Delay(loopSeconds * 1000, stoppingToken);
            }
        }

        private void Progress(FtpProgress obj)
        {
            if (!_showProgress)
                return;
            int barLength = 50;
            double percent = obj.Progress;
            int filledLength = (int)(barLength * percent / 100.0);

            string bar = new string('█', filledLength) + new string('░', barLength - filledLength);
            var totalMB = _fileDimensions / (1024.0 * 1024.0);
            double transferredMB = obj.TransferredBytes / (1024.0 * 1024.0);

            Console.Write($"\r[{bar}] {percent,6:0.0}%  {transferredMB:0.00}/{totalMB:0.00} MB  {obj.TransferSpeedToString()} MB/s");

            if (percent >= 100.0)
            {
                Console.WriteLine(); // Vai a capo al completamento
            }
        }

        private void KillMe()
        {
            _applicationLifetime.StopApplication();
        }
    }
}