using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// 图标必须由这里先落到本地再交给 MyImage：MyImage 的 http 分支是在 UI 线程上发起下载的，
    /// 一张卡一个图标，慢网络下整个启动器窗口会被它拖死。
    /// </summary>
    public static async Task<string?> DownloadIconAsync(string url, CancellationToken token = default)
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "pclce-assistant-icons");
            Directory.CreateDirectory(folder);
            var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..20];
            var path = Path.Combine(folder, key + ExtensionFor(url));
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

            var (bytes, error) = await GetBytesAsync(url, null, token);
            if (bytes is null || bytes.Length == 0) return null;
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ExtensionFor(string url)
    {
        var path = url.Split('?')[0];
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".svg" })
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return ext;

        return ".webp"; // Modrinth / CurseForge 的图标默认都是 webp，MyBitmap 会走 WIC 转换
    }

    public static async Task<(byte[]? Body, string? Error)> GetBytesAsync(string absoluteUrl, string? apiKey = null,
        CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUrl);
            if (!string.IsNullOrEmpty(apiKey)) request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            using var response = await Client.SendAsync(request, token);
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            return response.IsSuccessStatusCode
                ? (bytes, null)
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

    public static async Task<(string? Body, string? Error)> GetAsync(string absoluteUrl, string? apiKey = null,
        CancellationToken token = default)
    {
        var (bytes, error) = await GetBytesAsync(absoluteUrl, apiKey, token);
        return (bytes is null ? null : Encoding.UTF8.GetString(bytes), error);
    }
}
