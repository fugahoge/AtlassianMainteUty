using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace AtlassianMainteUty;

/// <summary>
/// HTTP 通信とリトライの共通処理。
/// リクエストを関数で渡し、必要に応じてリトライして結果を返す。
/// </summary>
public static class CommonHttp
{
  /// <summary>
  /// リトライ可能とみなす HTTP ステータス（408, 429, 5xx）
  /// </summary>
  private static bool IsRetryable(HttpStatusCode statusCode)
  {
    var code = (int)statusCode;
    return code == 408 || code == 429 || code >= 500;
  }

  /// <summary>
  /// 指定したリクエストを送信し、失敗時は最大 maxRetries 回までリトライする。
  /// リクエストは毎回 createRequest で生成すること（リトライごとに新しいインスタンスが必要なため）。
  /// </summary>
  /// <param name="client">HttpClient</param>
  /// <param name="createRequest">リクエストを生成する関数（リトライのたびに呼ばれる）</param>
  /// <param name="maxRetries">最大試行回数（1 でリトライなし）</param>
  /// <param name="cancellationToken">キャンセルトークン</param>
  /// <returns>最終的なレスポンスとボディ文字列</returns>
  public static async Task<(HttpResponseMessage Response, string Body)> ExecuteWithRetryAsync(
    HttpClient client,
    Func<HttpRequestMessage> createRequest,
    int maxRetries = 3,
    CancellationToken cancellationToken = default)
  {
    if (maxRetries < 1)
      maxRetries = 1;

    HttpResponseMessage? response = null;
    string body = "";

    for (var attempt = 1; attempt <= maxRetries; attempt++)
    {
      using var request = createRequest();
      response = await client.SendAsync(request, cancellationToken);
      body = await response.Content.ReadAsStringAsync(cancellationToken);

      if (response.IsSuccessStatusCode)
        return (response, body);

      var retryable = IsRetryable(response.StatusCode);
      if (attempt >= maxRetries || !retryable)
        return (response, body);

      var delayMs = attempt * 2000;
      await Task.Delay(delayMs, cancellationToken);
    }

    return (response!, body);
  }

  /// <summary>
  /// Basic 認証用の Authorization ヘッダ値を生成する
  /// </summary>
  /// <param name="email">API トークンを発行したアカウントのメールアドレス</param>
  /// <param name="token">API トークン</param>
  /// <returns>Authorization ヘッダ値</returns>
  public static AuthenticationHeaderValue CreateBasicAuth(string email, string token)
  {
    var credential = $"{email}:{token}";
    var bytes = Encoding.UTF8.GetBytes(credential);
    var encoded = Convert.ToBase64String(bytes);
    return new AuthenticationHeaderValue("Basic", encoded);
  }
}
