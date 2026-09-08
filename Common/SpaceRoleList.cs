namespace AtlassianMainteUty;

/// <summary>
/// space-role-list 形式の JSON 出力用データ
/// サイトのロール定義と、スペースごとのロール割当を表す
/// </summary>
public sealed record SpaceRoleList(
  string Format,
  string Version,
  string CreateDate,
  string RoleMode,
  IReadOnlyList<SpaceRoleCatalogItem> Roles,
  IReadOnlyList<SpaceRoleListSpace> Spaces);

/// <summary>
/// サイトで利用可能なスペースロール（space-role-request の roleId / roleName はここから選ぶ）
/// </summary>
public sealed record SpaceRoleCatalogItem(
  string RoleId,
  string RoleName,
  string Type,
  string Description,
  IReadOnlyList<string> Permissions);

/// <summary>
/// スペース 1 件分のロール割当
/// </summary>
public sealed record SpaceRoleListSpace(
  string SpaceId,
  string SpaceKey,
  string SpaceName,
  IReadOnlyList<SpaceRoleListAssignment> Assignments);

/// <summary>
/// スペースに対する 1 プリンシパル（ユーザー／グループ等）のロール割当
/// </summary>
public sealed record SpaceRoleListAssignment(
  string PrincipalType,
  string PrincipalId,
  string Name,
  string RoleId,
  string RoleName);
