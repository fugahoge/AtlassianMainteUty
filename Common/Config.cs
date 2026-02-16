using System.Text.Json;

namespace AtlassianMainteUty;

public class Config
{
  public AtlassianConfig Atlassian { get; set; } = new();
  public LogConfig Log { get; set; } = new();

  public static Config Load()
  {
    var configPath = Path.Combine(Directory.GetCurrentDirectory(), Common.ConfigFileName);

    if (!File.Exists(configPath))
    {
      throw new FileNotFoundException($"設定ファイルが見つかりません: {configPath}");
    }

    var json = File.ReadAllText(configPath);
    var config = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (config == null)
    {
      throw new InvalidOperationException("設定ファイルの読み込みに失敗しました");
    }

    return config;
  }
}

public class AtlassianConfig
{
  public string JiraBaseUrl { get; set; } = "https://your-domain.atlassian.net";
  public string AdminApiBaseUrl { get; set; } = "https://api.atlassian.com";
  public string JiraAuthEmail { get; set; } = string.Empty;
  public string ApiToken { get; set; } = string.Empty;
  public string ApiKey { get; set; } = string.Empty;
  public string OrgId { get; set; } = string.Empty;
  public int HttpTimeoutSeconds { get; set; } = 30;
}

public class LogConfig
{
  public string Level { get; set; } = "Information";
  public int RetainedFileCountLimit { get; set; } = 10;
}
