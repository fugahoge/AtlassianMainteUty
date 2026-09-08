namespace AtlassianMainteUty;

/// <summary>
/// Confluence のスペース
/// </summary>
public sealed record ConfluenceSpace(
  string Id,
  string Key,
  string Name,
  string Type,
  string Status);

/// <summary>
/// スペースに付与された個別権限（旧・粒度権限）の 1 件
/// operation は「キー:対象種別」（例: read:space）で表す
/// </summary>
public sealed record SpacePermissionGrant(
  string Id,
  string PrincipalType,
  string PrincipalId,
  string Operation);

/// <summary>
/// スペースロールの定義
/// </summary>
public sealed record SpaceRoleDefinition(
  string Id,
  string Type,
  string Name,
  string Description,
  IReadOnlyList<string> SpacePermissions);

/// <summary>
/// スペースへのロール割当
/// </summary>
public sealed record SpaceRoleAssignment(
  string RoleId,
  string PrincipalId,
  string PrincipalType);
