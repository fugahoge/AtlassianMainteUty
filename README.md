# AtlassianMainteUty

Atlassian Cloud にアクセスしてユーザーをグループに追加する、またはユーザーの所属グループを表示する CLI です。

## 前提条件

- .NET 10.0 SDK
- Atlassian 組織で「新しいユーザー管理」が有効であること
- 追加するユーザーはすでにサイトに招待済みであること

## 設定

| 変数 | 説明 |
|------|------|
| `JiraBaseUrl` | Jira サイトの URL（例: `https://your-domain.atlassian.net`） |
| `ConfluenceBaseUrl` | Confluence サイトの URL（例: `https://your-domain.atlassian.net/wiki`）。未設定の場合は `JiraBaseUrl` + `/wiki` を使用 |
| `AdminApiBaseUrl` | Admin API のベース URL（通常は `https://api.atlassian.com` のまま） |
| `JiraAuthEmail` | Jira API トークンを発行したアカウントのメールアドレス |
| `ApiToken` | [Atlassian の API トークン](https://id.atlassian.com/manage-profile/security/api-tokens)（Jira 用） |
| `ApiKey` | [Atlassian Administration の API キー](https://admin.atlassian.com)（Settings → API keys） |
| `OrgId` | 組織 ID（Atlassian Administration の API キー画面に表示） |

## ビルド

publishディレクトリ内に実行ファイルが生成されます：

```bash
dotnet publish AtlassianMainteUty.sln
```

## 実行

### SetUserToGroup.exe - ユーザーのグループを設定

```bash
.\publish\SetUserToGroup.exe <JSONファイル>
```

- 第一引数で user-group-request 形式の JSON ファイルを指定

### SetUserToGroupBat.exe - ユーザーのグループを設定(バッチファイル版)

SetUserToGroup と同じ仕様（user-group-request 形式の JSON）だが、WebAPI を直接呼び出さず、代わりに curl を実行するバッチファイル（.bat）を出力する。

```bash
.\publish\SetUserToGroupBat.exe <JSONファイル> [出力パス]
```

- 第一引数で JSON ファイルを指定、第二引数で出力パスを指定（省略時は `SetUserToGroup.bat`）
- 生成されたバッチファイルを実行すると、curl で WebAPI と等価な処理を行う
- 前提: curl がインストールされていること、Config.json の設定が正しいこと

### SetUserToGroupPS.exe - ユーザーのグループを設定(PowerShell版)

SetUserToGroup と同じ仕様（user-group-request 形式の JSON）だが、WebAPI を直接呼び出さず、代わりに PowerShell スクリプト（.ps1）を出力する。

```bash
.\publish\SetUserToGroupPS.exe <JSONファイル> [出力パス]
```

- 第一引数で JSON ファイルを指定、第二引数で出力パスを指定（省略時は `SetUserToGroup.ps1`）
- 生成された PowerShell スクリプトを実行すると、Invoke-RestMethod で WebAPI と等価な処理を行う
- accountId/groupId をキャッシュして API 呼び出し回数を削減
- 前提: PowerShell 5.1 以上、Config.json の設定が正しいこと

### GetSpacePerms.exe - Confluence スペースの旧権限を取得

Confluence の全スペースについて、スペースに直接付与された個別権限（旧・粒度権限、14 権限）を `space-permission-list` 形式の JSON で出力する。ロールベースアクセス（RBAC）への移行前の棚卸しに使う。

```bash
.\publish\GetSpacePerms.exe [出力パス]
```

- 第一引数で出力パスを指定（省略時は `space-permissions.json`）
- `permissions` は `操作:対象種別`（例: `create:page`）の形式
- グループ ID / accountId は可能な範囲でグループ名・メールアドレスに解決して `name` に出力する

有効な `操作:対象種別` の組み合わせは以下の 14 通り。

| 操作 | 対象種別 |
|------|----------|
| `read` | `space` |
| `create` | `page` / `blogpost` / `comment` / `attachment` |
| `delete` | `page` / `blogpost` / `comment` / `attachment` / `space` |
| `export` | `space` |
| `administer` | `space` |
| `archive` | `page` |
| `restrict_content` | `space` |

`delete:space` は「スペースの削除」ではなく Delete Own（自分のコンテンツの削除）、`restrict_content:space` は Add/Delete Restrictions を指す。

### GetSpaceRoles.exe - Confluence スペースの新権限（ロール）を取得

サイトのロール定義と、全スペースのロール割当を `space-role-list` 形式の JSON で出力する。

```bash
.\publish\GetSpaceRoles.exe [出力パス]
```

- 第一引数で出力パスを指定（省略時は `space-roles.json`）
- `roleMode` にサイトのアクセスモード（pre-roles / roles transition / roles only）が入る
- `roles` に利用可能なロール定義の一覧が入る。ここの `roleId` / `roleName` を見て SetSpaceRoles 用の JSON を作成する
- `spaces[].assignments[]` に各スペースの現在のロール割当が入る

### SetSpaceRoles.exe - Confluence スペースにロールを割り当て

`space-role-request` 形式の JSON を読み込み、`POST /wiki/api/v2/spaces/{id}/role-assignments` でスペースロールを割り当てる。

```bash
.\publish\SetSpaceRoles.exe <JSONファイル>
```

- 第一引数で `space-role-request` 形式の JSON ファイルを指定
- スペースは `spaceId`、未指定なら `spaceKey` から解決する
- プリンシパルは `principalId`、未指定なら `mail`（USER）／ `groupName`（GROUP）から解決する
- ロールは `roleId`、未指定なら `roleName` から解決する
- 前提: サイトでロールベースアクセスが有効であること（`roleMode` が pre-roles のままだと割り当てできない）

### GetUserGroups.exe - テナント全ユーザーの所属グループを表示


```bash
.\publish\GetUserGroups.exe
```

## 動作

## リトライ条件

- HTTP 408 (Request Timeout)
- HTTP 429 (Too Many Requests)
- HTTP 5xx (サーバーエラー)

上記のいずれかの場合に、３回までリトライします。

## ロールベースアクセスへの移行手順

Confluence Cloud のスペース権限は、14 個の個別権限を割り当てる方式から、ロール（Admin / Manager / Collaborator / Viewer とカスタムロール）を割り当てる方式へ移行する。

1. `GetSpacePerms.exe old-perms.json` で現状の旧権限を取得する
2. `GetSpaceRoles.exe roles-before.json` でロール定義と現在の割当を取得する
3. `old-perms.json` の権限と `roles-before.json` の `roles` を突き合わせ、どのプリンシパルにどのロールを割り当てるかを決めて `space-role-request` 形式の JSON を作成する（`sample_space-role-request.json` 参照）
4. `SetSpaceRoles.exe request.json` でロールを割り当てる
5. `GetSpaceRoles.exe roles-after.json` で再取得し、`assignments` が意図どおりか確認する
