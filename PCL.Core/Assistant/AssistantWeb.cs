using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net.Http;

namespace PCL.Core.Assistant;

/// <summary>
/// 助手自己发起的联网统一走这里：串行、系统 DNS、失败只回错误文本。
/// </summary>
public static class AssistantWeb
{
    // 一次渲染里给同一个 API 并发十几个连接时，上游的 DoH 连接竞速会有整组失败的情况，
    // 而那条路径是 LogWrapper.Error + 错误对话框；助手的批量取数不该把用户推到那个框上。
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        Proxy = HttpProxyManager.Instance,
        UseProxy = true,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static async Task<(string? Body, string? Error)> GetAsync(string absoluteUrl, string? apiKey = null,
        CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUrl);
            if (!string.IsNullOrEmpty(apiKey)) request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            using var response = await Client.SendAsync(request, token);
            var text = await response.Content.ReadAsStringAsync(token);
            return response.IsSuccessStatusCode
                ? (text, null)
                : (null, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return (null, e.Message);
        }
        finally
        {
            Gate.Release();
        }
    }
}
