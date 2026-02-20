using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AtlassianMainteUty;

/// <summary>
/// SetUserToGroup と同じ仕様で、WebAPI を直接呼び出さず、
/// 代わりに PowerShell スクリプトを出力するプログラム。
/// 生成されたスクリプトは accountId/groupId をキャッシュして API 呼び出し回数を削減する。
/// </summary>
internal static class SetUserToGroupPS
{
  private static Config? _config;
  private static ILogger? _logger;

  public static async Task<int> Main(string[] args)
  {
    try
    {
      _config = Config.Load();
      _logger = Log.CreateLogger("SetUserToGroupPS.log");

      var buildDate = Log.GetBuildDate(Assembly.GetExecutingAssembly());
      if (!string.IsNullOrEmpty(buildDate))
        _logger.LogInformation("ビルド日時: {BuildDate}", buildDate);

      if (args.Length < 1)
      {
        _logger.LogError("使用法: SetUserToGroupPS <JSONファイル> [出力パス]");
        Console.WriteLine("使用法: SetUserToGroupPS <JSONファイル> [出力パス]");
        return 1;
      }

      var jsonPath = args[0].Trim();
      var request = await ReadJsonFile(jsonPath);
      if (request == null)
        return 1;

      var outputPath = args.Length > 1 ? args[1] : "SetUserToGroup.ps1";
      var scriptContent = GeneratePowerShellScript(request);
      await File.WriteAllTextAsync(outputPath, scriptContent, new UTF8Encoding(false));

      _logger.LogInformation("PowerShell スクリプトを出力しました: {Path}", Path.GetFullPath(outputPath));
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

  private static string GeneratePowerShellScript(UserGroupRequest request)
  {
    var config = _config!.Atlassian;
    var sb = new StringBuilder();

    var jiraUrl = config.JiraBaseUrl.TrimEnd('/');
    var adminUrl = config.AdminApiBaseUrl.TrimEnd('/');
    var jiraAuth = $"{config.JiraAuthEmail}:{config.ApiToken}";
    var orgId = config.OrgId;
    var apiKey = config.ApiKey;

    sb.AppendLine("#Requires -Version 5.1");
    sb.AppendLine("# SetUserToGroup と等価な処理を PowerShell で実行（accountId/groupId をキャッシュして API 呼び出しを削減）");
    sb.AppendLine();
    sb.AppendLine("$ErrorActionPreference = 'Stop'");
    sb.AppendLine("Set-Location $PSScriptRoot");
    sb.AppendLine();
    sb.AppendLine("# 設定（プログラム実行時に Config.json から読み込んだ値）");
    sb.AppendLine($"$jiraUrl = '{EscapeForPowerShellString(jiraUrl)}'");
    sb.AppendLine($"$jiraAuth = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('{EscapeForPowerShellString(jiraAuth)}'))");
    sb.AppendLine($"$adminUrl = '{EscapeForPowerShellString(adminUrl)}'");
    sb.AppendLine($"$adminKey = '{EscapeForPowerShellString(apiKey)}'");
    sb.AppendLine($"$orgId = '{EscapeForPowerShellString(orgId)}'");
    sb.AppendLine();
    sb.AppendLine("# キャッシュ（API 呼び出し回数の削減）");
    sb.AppendLine("$accountCache = @{}");
    sb.AppendLine("$groupCache = @{}");
    sb.AppendLine();
    sb.AppendLine("function Get-AccountId {");
    sb.AppendLine("  param([string]$Email)");
    sb.AppendLine("  if ($accountCache.ContainsKey($Email)) { return $accountCache[$Email] }");
    sb.AppendLine("  $q = [uri]::EscapeDataString($Email)");
    sb.AppendLine("  $r = Invoke-RestMethod -Uri \"$jiraUrl/rest/api/3/users/search?query=$q\" -Headers @{ Authorization = \"Basic $jiraAuth\" }");
    sb.AppendLine("  $id = if ($r -and $r.Count -gt 0) { $r[0].accountId } else { $null }");
    sb.AppendLine("  if ($id) { $accountCache[$Email] = $id }");
    sb.AppendLine("  return $id");
    sb.AppendLine("}");
    sb.AppendLine();
    sb.AppendLine("function Get-GroupId {");
    sb.AppendLine("  param([string]$GroupName)");
    sb.AppendLine("  if ($groupCache.ContainsKey($GroupName)) { return $groupCache[$GroupName] }");
    sb.AppendLine("  $q = [uri]::EscapeDataString($GroupName)");
    sb.AppendLine("  $r = Invoke-RestMethod -Uri \"$jiraUrl/rest/api/3/groupuserpicker?query=$q\" -Headers @{ Authorization = \"Basic $jiraAuth\" }");
    sb.AppendLine("  $id = $r.groups.groups | Where-Object { $_.name -eq $GroupName } | Select-Object -ExpandProperty groupId -First 1");
    sb.AppendLine("  if ($id) { $groupCache[$GroupName] = $id }");
    sb.AppendLine("  return $id");
    sb.AppendLine("}");
    sb.AppendLine();
    sb.AppendLine("function Get-UserGroupNames {");
    sb.AppendLine("  param([string]$AccountId)");
    sb.AppendLine("  $r = Invoke-RestMethod -Uri \"$jiraUrl/rest/api/3/groups/picker?accountId=$AccountId\" -Headers @{ Authorization = \"Basic $jiraAuth\" }");
    sb.AppendLine("  $grps = $r.groups");
    sb.AppendLine("  if ($grps -is [array]) { return $grps | ForEach-Object { $_.name } }");
    sb.AppendLine("  return $grps.groups | ForEach-Object { $_.name }");
    sb.AppendLine("}");
    sb.AppendLine();

    foreach (var user in request.Users)
    {
      if (string.IsNullOrEmpty(user.Mail))
        continue;

      var email = user.Mail;
      var emailEsc = EscapeForPowerShellString(email);

      sb.AppendLine("# === ユーザー: " + emailEsc + " ===");

      if (user.DeleteAll)
      {
        sb.AppendLine("$accountId = Get-AccountId '" + emailEsc + "'");
        sb.AppendLine("if ($accountId) {");
        sb.AppendLine("  $groupNames = Get-UserGroupNames -AccountId $accountId");
        sb.AppendLine("  foreach ($gn in $groupNames) {");
        sb.AppendLine("    $gid = Get-GroupId $gn");
        sb.AppendLine("    if ($gid) {");
        sb.AppendLine("      Write-Host \"グループ削除: " + emailEsc + " <- $gn\"");
        sb.AppendLine("      Invoke-RestMethod -Method Delete -Uri \"$adminUrl/admin/v1/orgs/$orgId/directory/groups/$gid/memberships/$accountId\" -Headers @{ Authorization = \"Bearer $adminKey\" }");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("} else { Write-Warning \"ユーザーが見つかりません: " + emailEsc + "\" }");
      }
      else
      {
        foreach (var groupName in user.DelGroup ?? [])
        {
          if (string.IsNullOrWhiteSpace(groupName))
            continue;
          var gn = groupName.Trim();
          var gnEsc = EscapeForPowerShellString(gn);
          sb.AppendLine("$accountId = Get-AccountId '" + emailEsc + "'; $gid = Get-GroupId '" + gnEsc + "'");
          sb.AppendLine("if ($accountId -and $gid) {");
          sb.AppendLine("  Write-Host \"グループ削除: " + emailEsc + " <- " + gnEsc + "\"");
          sb.AppendLine("  Invoke-RestMethod -Method Delete -Uri \"$adminUrl/admin/v1/orgs/$orgId/directory/groups/$gid/memberships/$accountId\" -Headers @{ Authorization = \"Bearer $adminKey\" }");
          sb.AppendLine("} elseif (-not $accountId) { Write-Warning \"ユーザーが見つかりません: " + emailEsc + "\" } elseif (-not $gid) { Write-Warning \"グループが見つかりません: " + gnEsc + "\" }");
        }

        foreach (var groupName in user.AddGroup ?? [])
        {
          if (string.IsNullOrWhiteSpace(groupName))
            continue;
          var gn = groupName.Trim();
          var gnEsc = EscapeForPowerShellString(gn);
          sb.AppendLine("$accountId = Get-AccountId '" + emailEsc + "'; $gid = Get-GroupId '" + gnEsc + "'");
          sb.AppendLine("if ($accountId -and $gid) {");
          sb.AppendLine("  Write-Host \"グループ追加: " + emailEsc + " -> " + gnEsc + "\"");
          sb.AppendLine("  $body = @{ account_id = $accountId } | ConvertTo-Json");
          sb.AppendLine("  try {");
          sb.AppendLine("    Invoke-RestMethod -Method Post -Uri \"$adminUrl/admin/v1/orgs/$orgId/directory/groups/$gid/memberships\" -Headers @{ Authorization = \"Bearer $adminKey\"; 'Content-Type' = 'application/json' } -Body $body");
          sb.AppendLine("  } catch { if ($_.Exception.Response.StatusCode -eq 400 -and $_.ErrorDetails.Message -match 'already a member') { Write-Host '  既に所属しています' } else { throw } }");
          sb.AppendLine("} elseif (-not $accountId) { Write-Warning \"ユーザーが見つかりません: " + emailEsc + "\" } elseif (-not $gid) { Write-Warning \"グループが見つかりません: " + gnEsc + "\" }");
        }
      }

      sb.AppendLine();
    }

    sb.AppendLine("Write-Host '処理が完了しました。'");

    return sb.ToString();
  }

  /// <summary>PowerShell の単一引用符文字列内用のエスケープ（' を '' に）</summary>
  private static string EscapeForPowerShellString(string value)
  {
    if (string.IsNullOrEmpty(value))
      return value;
    return value.Replace("'", "''");
  }
}
