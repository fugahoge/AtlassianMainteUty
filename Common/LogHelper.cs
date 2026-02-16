using System.Reflection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AtlassianMainteUty;

/// <summary>
/// ログとビルド日時取得の共通ヘルパー。
/// </summary>
public static class LogHelper
{
  /// <summary>
  /// Serilog を使用した ILogger を作成する。
  /// </summary>
  /// <param name="logFileName">ログファイル名（例: GetUserGroups.log）</param>
  /// <returns>ILogger</returns>
  public static Microsoft.Extensions.Logging.ILogger CreateLogger(string logFileName)
  {
    var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
    Directory.CreateDirectory(logDirectory);

    var logLevel = "Information";
    var retainedFileCountLimit = 10;

    var minimumLevel = logLevel switch
    {
      "Verbose" => Serilog.Events.LogEventLevel.Verbose,
      "Debug" => Serilog.Events.LogEventLevel.Debug,
      "Information" => Serilog.Events.LogEventLevel.Information,
      "Warning" => Serilog.Events.LogEventLevel.Warning,
      "Error" => Serilog.Events.LogEventLevel.Error,
      "Fatal" => Serilog.Events.LogEventLevel.Fatal,
      _ => Serilog.Events.LogEventLevel.Information
    };

    Log.Logger = new LoggerConfiguration()
      .MinimumLevel.Is(minimumLevel)
      .WriteTo.Console()
      .WriteTo.File(
        Path.Combine(logDirectory, logFileName),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: retainedFileCountLimit,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
      .CreateLogger();

    var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
    return loggerFactory.CreateLogger("App");
  }

  /// <summary>
  /// アセンブリメタデータからビルド日時を取得する。
  /// </summary>
  /// <param name="assembly">ビルド日時を取得するアセンブリ（通常は Assembly.GetExecutingAssembly()）</param>
  /// <returns>ビルド日時文字列。取得できない場合は空文字</returns>
  public static string GetBuildDate(Assembly assembly)
  {
    try
    {
      foreach (var attr in assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute)))
      {
        if (attr is AssemblyMetadataAttribute metadataAttr && metadataAttr.Key == "BuildDate")
          return metadataAttr.Value ?? string.Empty;
      }
    }
    catch
    {
      // エラーが発生した場合は空文字を返す
    }
    return string.Empty;
  }
}
