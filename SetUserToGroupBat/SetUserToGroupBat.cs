using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// SetUserToGroup と同じ仕様で、WebAPI を直接呼び出さず、
/// 代わりに curl を実行するバッチファイルを出力するプログラム。
/// </summary>
internal static class SetUserToGroupBat
{
  private static Config? _config;
  private static ILogger? _logger;

  public static async Task<int> Main(string[] args)
  {
    try
    {
      _config = Config.Load();
      _logger = Log.CreateLogger("SetUserToGroupBat.log");

      var buildDate = Log.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
        _logger.LogInformation("ビルド日時: {BuildDate}", buildDate);

      var request = await ReadJsonFile("input.json");
      if (request == null)
        return 1;

      var outputPath = args.Length > 0 ? args[0] : "SetUserToGroup.bat";
      var batContent = GenerateBatchFile(request);
      await File.WriteAllTextAsync(outputPath, batContent, Encoding.UTF8);

      _logger.LogInformation("バッチファイルを出力しました: {Path}", Path.GetFullPath(outputPath));
      return 0;
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "エラーが発生しました");
      return 1;
    }
    finally
    {
      Serilog.Log.CloseAndFlush();
    }
  }

  private static async Task<UserGroupRequest?> ReadJsonFile(string filePath)
  {
    if (!File.Exists(filePath))
    {
      _logger?.LogError("{FilePath} が見つかりません。", filePath);
      throw new FileNotFoundException(filePath);
    }

    try
    {
      var json = await File.ReadAllTextAsync(filePath);
      var options = new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };
      var request = JsonSerializer.Deserialize<UserGroupRequest>(json, options);

      if (request?.Users == null || request.Users.Count == 0)
      {
        _logger?.LogError("json にユーザーが含まれていません。");
        throw new InvalidOperationException("json contains no users");
      }

      if (request.Format != "user-group-request")
      {
        _logger?.LogError("json の format が不正です。");
        throw new InvalidOperationException("format is invalid");
      }
      if (request.Version != "1.0")
      {
        _logger?.LogError("json の version が不正です。");
        throw new InvalidOperationException("version is invalid");
      }

      return request;
    }
    catch (JsonException ex)
    {
      _logger?.LogError(ex, "json のパースに失敗しました。");
      throw;
    }
  }

  private static string GenerateBatchFile(UserGroupRequest request)
  {
    var config = _config!.Atlassian;
    var sb = new StringBuilder();

    var jiraUrl = config.JiraBaseUrl.TrimEnd('/');
    var adminUrl = config.AdminApiBaseUrl.TrimEnd('/');
    var jiraAuth = $"{config.JiraAuthEmail}:{config.ApiToken}";
    var orgId = config.OrgId;

    sb.AppendLine("@echo off");
    sb.AppendLine("setlocal EnableDelayedExpansion");
    sb.AppendLine("cd /d \"%~dp0\"");
    sb.AppendLine();
    sb.AppendLine("REM 設定（プログラム実行時に Config.json から読み込んだ値）");
    sb.AppendLine($"set \"JIRA_URL={EscapeForBatch(jiraUrl)}\"");
    sb.AppendLine($"set \"JIRA_AUTH={EscapeForBatch(jiraAuth)}\"");
    sb.AppendLine($"set \"ADMIN_URL={EscapeForBatch(adminUrl)}\"");
    sb.AppendLine($"set \"ADMIN_KEY={EscapeForBatch(config.ApiKey)}\"");
    sb.AppendLine($"set \"ORG_ID={EscapeForBatch(orgId)}\"");
    sb.AppendLine();
    sb.AppendLine("set \"TEMP_JSON=%TEMP%\\SetUserToGroupBat_%RANDOM%.json\"");
    sb.AppendLine();

    foreach (var user in request.Users)
    {
      if (string.IsNullOrEmpty(user.Mail))
        continue;

      var emailEnc = Uri.EscapeDataString(user.Mail);
      var emailEncForBatch = EscapeForBatch(emailEnc);
      sb.AppendLine($"REM === ユーザー: {user.Mail} ===");
      sb.AppendLine($"set \"EMAIL_ENC={emailEncForBatch}\"");
      sb.AppendLine();

      if (user.DeleteAll)
      {
        AppendDeleteAllBlock(sb, user.Mail);
      }
      else
      {
        foreach (var groupName in user.DelGroup ?? [])
        {
          if (string.IsNullOrWhiteSpace(groupName))
            continue;
          AppendRemoveFromGroupBlock(sb, user.Mail, groupName.Trim());
        }
      }

      foreach (var groupName in user.AddGroup ?? [])
      {
        if (string.IsNullOrWhiteSpace(groupName))
          continue;
        AppendAddToGroupBlock(sb, user.Mail, groupName.Trim());
      }

      sb.AppendLine();
    }

    sb.AppendLine("echo 処理が完了しました。");
    sb.AppendLine("endlocal");
    sb.AppendLine("exit /b 0");

    return sb.ToString();
  }

  private static void AppendDeleteAllBlock(StringBuilder sb, string email)
  {
    sb.AppendLine("REM deleteAll: 所属する全グループから削除");
    sb.AppendLine();
    sb.AppendLine("REM accountId を取得");
    sb.AppendLine("curl -s -u \"!JIRA_AUTH!\" \"!JIRA_URL!/rest/api/3/users/search?query=!EMAIL_ENC!\" -o \"!TEMP_JSON!\"");
    sb.AppendLine("for /f \"delims=\" %%a in ('powershell -NoProfile -Command \"try { (Get-Content '!TEMP_JSON!' -Raw | ConvertFrom-Json)[0].accountId } catch { '' }\"') do set \"ACCOUNT_ID=%%a\"");
    sb.AppendLine("if \"!ACCOUNT_ID!\"==\"\" (echo ユーザーが見つかりません: " + EscapeForBatch(email) + " & goto :eof)");
    sb.AppendLine();
    sb.AppendLine("REM 所属グループ一覧を取得");
    sb.AppendLine("curl -s -u \"!JIRA_AUTH!\" \"!JIRA_URL!/rest/api/3/groups/picker?accountId=!ACCOUNT_ID!\" -o \"!TEMP_JSON!\"");
    sb.AppendLine("for /f \"delims=\" %%g in ('powershell -NoProfile -Command \"$j=Get-Content '!TEMP_JSON!' -Raw|ConvertFrom-Json; $grps=$j.groups; if($grps -is [array]){$grps} else {$grps.groups}; $grps|%%{$_.name}\"') do (");
    sb.AppendLine("  set \"GROUP_NAME=%%g\"");
    sb.AppendLine("  set \"GROUP_NAME_PS=!GROUP_NAME:'=''!\"");
    sb.AppendLine("  REM groupId を取得して削除");
    sb.AppendLine("  curl -s -G -u \"!JIRA_AUTH!\" --data-urlencode \"query=!GROUP_NAME!\" \"!JIRA_URL!/rest/api/3/groupuserpicker\" -o \"!TEMP_JSON!\"");
    sb.AppendLine("  for /f \"delims=\" %%i in ('powershell -NoProfile -Command \"$j=Get-Content '!TEMP_JSON!' -Raw|ConvertFrom-Json; $j.groups.groups|?{$_.name -eq '!GROUP_NAME_PS!'}|%%{$_.groupId}\"') do (");
    sb.AppendLine("    echo グループ削除: " + EscapeForBatch(email) + " ^<- %%g");
    sb.AppendLine("    curl -s -X DELETE -H \"Authorization: Bearer !ADMIN_KEY!\" \"!ADMIN_URL!/admin/v1/orgs/!ORG_ID!/directory/groups/%%i/memberships/!ACCOUNT_ID!\"");
    sb.AppendLine("  )");
    sb.AppendLine(")");
    sb.AppendLine();
  }

  private static void AppendRemoveFromGroupBlock(StringBuilder sb, string email, string groupName)
  {
    var groupEnc = EscapeForBatch(Uri.EscapeDataString(groupName));
    sb.AppendLine($"REM グループ削除: {email} <- {groupName}");
    sb.AppendLine("curl -s -u \"!JIRA_AUTH!\" \"!JIRA_URL!/rest/api/3/users/search?query=!EMAIL_ENC!\" -o \"!TEMP_JSON!\"");
    sb.AppendLine("for /f \"delims=\" %%a in ('powershell -NoProfile -Command \"try { (Get-Content '!TEMP_JSON!' -Raw | ConvertFrom-Json)[0].accountId } catch { '' }\"') do set \"ACCOUNT_ID=%%a\"");
    sb.AppendLine("if \"!ACCOUNT_ID!\"==\"\" (echo ユーザーが見つかりません: " + EscapeForBatch(email) + " & goto :eof)");
    sb.AppendLine("curl -s -u \"!JIRA_AUTH!\" \"!JIRA_URL!/rest/api/3/groupuserpicker?query=" + groupEnc + "\" -o \"!TEMP_JSON!\"");
    sb.AppendLine("for /f \"delims=\" %%i in ('powershell -NoProfile -Command \"$j=Get-Content '!TEMP_JSON!' -Raw|ConvertFrom-Json; $j.groups.groups|?{$_.name -eq '" + EscapeForPowerShell(groupName) + "'}|%{$_.groupId}\"') do (");
    sb.AppendLine("  curl -s -X DELETE -H \"Authorization: Bearer !ADMIN_KEY!\" \"!ADMIN_URL!/admin/v1/orgs/!ORG_ID!/directory/groups/%%i/memberships/!ACCOUNT_ID!\"");
    sb.AppendLine(")");
    sb.AppendLine();
  }

  private static void AppendAddToGroupBlock(StringBuilder sb, string email, string groupName)
  {
    var groupEnc = EscapeForBatch(Uri.EscapeDataString(groupName));
    sb.AppendLine($"REM グループ追加: {email} -> {groupName}");
    sb.AppendLine("curl -s -u \"!JIRA_AUTH!\" \"!JIRA_URL!/rest/api/3/users/search?query=!EMAIL_ENC!\" -o \"!TEMP_JSON!\"");
    sb.AppendLine("for /f \"delims=\" %%a in ('powershell -NoProfile -Command \"try { (Get-Content '!TEMP_JSON!' -Raw | ConvertFrom-Json)[0].accountId } catch { '' }\"') do set \"ACCOUNT_ID=%%a\"");
    sb.AppendLine("if \"!ACCOUNT_ID!\"==\"\" (echo ユーザーが見つかりません: " + EscapeForBatch(email) + " & goto :eof)");
    sb.AppendLine("curl -s -u \"!JIRA_AUTH!\" \"!JIRA_URL!/rest/api/3/groupuserpicker?query=" + groupEnc + "\" -o \"!TEMP_JSON!\"");
    sb.AppendLine("for /f \"delims=\" %%i in ('powershell -NoProfile -Command \"$j=Get-Content '!TEMP_JSON!' -Raw|ConvertFrom-Json; $j.groups.groups|?{$_.name -eq '" + EscapeForPowerShell(groupName) + "'}|%{$_.groupId}\"') do (");
    sb.AppendLine("  curl -s -X POST -H \"Authorization: Bearer !ADMIN_KEY!\" -H \"Content-Type: application/json\" -d \"{\\\"account_id\\\":\\\"!ACCOUNT_ID!\\\"}\" \"!ADMIN_URL!/admin/v1/orgs/!ORG_ID!/directory/groups/%%i/memberships\"");
    sb.AppendLine(")");
    sb.AppendLine();
  }

  /// <summary>バッチファイル用のエスケープ（% を %% に）</summary>
  private static string EscapeForBatch(string value)
  {
    if (string.IsNullOrEmpty(value))
      return value;
    return value.Replace("%", "%%");
  }

  /// <summary>PowerShell 文字列内用のエスケープ（' を '' に）</summary>
  private static string EscapeForPowerShell(string value)
  {
    if (string.IsNullOrEmpty(value))
      return value;
    return value.Replace("'", "''");
  }
}
