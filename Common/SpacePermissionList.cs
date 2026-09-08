namespace AtlassianMainteUty;

/// <summary>
/// space-permission-list 形式の JSON 出力用データ
/// スペースに直接付与された個別権限（旧・粒度権限）を表す
/// </summary>
public sealed record SpacePermissionList(
  string Format,
  string Version,
  string CreateDate,
  IReadOnlyList<SpacePermissionSpace> Spaces);

/// <summary>
/// スペース 1 件分の個別権限
/// </summary>
public sealed record SpacePermissionSpace(
  string SpaceId,
  string SpaceKey,
  string SpaceName,
  IReadOnlyList<SpacePermissionPrincipal> Principals);

/// <summary>
/// スペースに対する 1 プリンシパル（ユーザー／グループ等）の個別権限
/// permissions は「操作:対象種別」（例: create:page）で表す
/// </summary>
public sealed record SpacePermissionPrincipal(
  string PrincipalType,
  string PrincipalId,
  string Name,
  IReadOnlyList<string> Permissions);
