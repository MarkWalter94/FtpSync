using FluentFTP;

namespace FtpSync
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly IServiceScopeFactory _serviceScopeFactory;

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


            var server = string.Empty;
            var user = string.Empty;
            var pwd = string.Empty;

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


            initScope.Dispose();
            var folderFilesToUpload = _configuration["Files:FilesToUploadFolder"];
            var targetFtp = _configuration["Files:TargetFolderFtp"];
            var maxRetries = _configuration.GetValue<int>("Settings:MaxRetries");
            var deleteAfterTransfer = _configuration.GetValue<bool>("Settings:DeleteAfterTransfer");
            var loopSeconds = _configuration.GetValue<int>("Settings:LoopSeconds");

            if (!Directory.Exists(folderFilesToUpload))
            {
                _logger.LogError($"Dir {folderFilesToUpload} does not exists, stopping!");
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

                    foreach (var file in Directory.EnumerateFiles(folderFilesToUpload, "*.*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(file);
                        var fileSize = fi.Length;
                        var lastModDate = fi.LastWriteTimeUtc;
                        
                        var fullPathFile = Path.GetFullPath(file);
                        var fileStat = await dbUtils.GetFileSyncStatus(Path.GetFullPath(fullPathFile), fileSize, lastModDate);
                        if (fileStat == -1)
                        {
                            //Già sincronizzato.
                            _logger.LogDebug($"File {file} already synched, skipping..");
                            continue;
                        }
                        else if (fileStat == -2 || fileStat == -3)
                        {
                            _logger.LogDebug($"File {file} modified, replacing..");
                        }
                        else if (fileStat > maxRetries)
                        {
                            _logger.LogDebug($"File {file} max retries reached, skipping..");
                            continue; //Troppi tentativi.
                        }

                        _logger.LogDebug($"Sync of file {file}...");

                        //Carico il file..
                        try
                        {
                            if (!client.IsConnected)
                                client.AutoConnect();

                            var result = await Task.Run(() => client.UploadFile(fullPathFile, Path.Combine(targetFtp, Path.GetRelativePath(folderFilesToUpload, fullPathFile)), FtpRemoteExists.Overwrite, true));
                            _logger.LogDebug("File synched!");
                            await dbUtils.MarkFileAsSynhronized(fullPathFile, fileSize, lastModDate, true, null);
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
                            await dbUtils.MarkFileAsSynhronized(fullPathFile, fileSize, lastModDate, false, errstr);
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

        private void KillMe()
        {
            _applicationLifetime.StopApplication();
        }
    }
}