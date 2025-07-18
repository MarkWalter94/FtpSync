using System.CommandLine;
using System.Security.Cryptography;
using System.Text;
using FtpSync;
using NLog.Extensions.Logging;

var argsPassed = true;
FtpSettings? ftpSettings = null;
if (args.Any())
{
    argsPassed = false;
    var serverOption = new Option<string>(
        name: "--server",
        description: "The FTP server address")
    {
        IsRequired = true
    };
    var userOption = new Option<string>(
        name: "--user",
        description: "The FTP username")
    {
        IsRequired = true
    };
    var passwordOption = new Option<string>(
        name: "--password",
        description: "The FTP password")
    {
        IsRequired = true
    };
    
    var rootCommand = new RootCommand
    {
        serverOption,
        userOption,
        passwordOption
    };

    rootCommand.SetHandler((server, user, password) =>
    {
        ftpSettings = new FtpSettings
        {
            Entropy = GenerateStrongRandomString(37),
            Server = server,
            User = user,
            Password = password
        };

        argsPassed = true;
        // Set the configuration in the host
    }, serverOption, userOption, passwordOption);
    var res = await rootCommand.InvokeAsync(args);
    if (res != 0)
        return res;
}

while (!argsPassed)
    await Task.Delay(100);

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddNLog();
    })
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>();
        services.AddScoped<IDbUtils, DbUtils>();
        services.AddScoped<IEncriptionUtils, EncriptionUtils>();
        if (ftpSettings != null)
        {
            services.AddSingleton(ftpSettings);
        }
        services.AddWindowsService();
    })
    .Build();


await host.RunAsync();

return 0;


static string GenerateStrongRandomString(int length)
{
    const string validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+-=[]{}|;:,.<>?";
    var result = new StringBuilder(length);
    var buffer = new byte[4];

    for (int i = 0; i < length; i++)
    {
        RandomNumberGenerator.Fill(buffer);
        var randomIndex = BitConverter.ToUInt32(buffer, 0) % validChars.Length;
        result.Append(validChars[(int)randomIndex]);
    }

    return result.ToString();
}