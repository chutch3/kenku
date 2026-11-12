using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Repository;
using Xunit;

namespace API.Tests.Unit;

public class LoggingConfigTests
{
    [Fact]
    public void TheLogFileLivesUnderTheAppDataWorkingDirectory()
    {
        KenkuSettings settings = new() { AppData = "/data" };

        Assert.Equal(Path.Join("/data", "kenku-api", "log", "Kenku.log"), settings.LogFilePath);
    }

    [Fact]
    public void TheShippedConfigWritesToTheConfiguredLogFile()
    {
        string logFile = Path.Join(Path.GetTempPath(), $"kenku-log-{Guid.NewGuid():N}", "Kenku.log");
        GlobalContext.Properties[KenkuSettings.LogFileProperty] = logFile;
        ILoggerRepository repository = LogManager.CreateRepository(Guid.NewGuid().ToString());
        try
        {
            XmlConfigurator.Configure(repository, new FileInfo("Log4Net.config.xml"));

            RollingFileAppender appender = Assert.Single(repository.GetAppenders().OfType<RollingFileAppender>());
            Assert.Equal(logFile, appender.File);
        }
        finally
        {
            repository.Shutdown();
            if (Directory.Exists(Path.GetDirectoryName(logFile)))
                Directory.Delete(Path.GetDirectoryName(logFile)!, true);
        }
    }
}
