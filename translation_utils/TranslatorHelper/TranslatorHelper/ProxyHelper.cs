using System;
using System.Net;
using System.Net.Http;

/// <summary>
/// 代理工具：自动检测系统代理并在 HttpClient / Git 操作中复用。
/// </summary>
public static class ProxyHelper
{
    private static readonly Uri GithubApiUri = new("https://api.github.com");
    private static IWebProxy? _detectedProxy;
    private static bool _proxyDetected;

    /// <summary>
    /// 尝试检测系统代理（WebRequest 默认代理 & 环境变量 & Windows 设置）。
    /// </summary>
    public static IWebProxy? DetectSystemProxy()
    {
        if (_proxyDetected)
        {
            return _detectedProxy;
        }

        _detectedProxy = TryGetProxy(WebRequest.DefaultWebProxy, "[提示] 检测到系统默认代理");

        if (_detectedProxy == null)
        {
            _detectedProxy = TryGetProxyFromEnvironment();
        }

        if (_detectedProxy == null)
        {
            _detectedProxy = TryGetProxy(WebRequest.GetSystemWebProxy(), "[提示] 检测到 Windows 代理设置");
        }

        _proxyDetected = true;

        if (_detectedProxy == null)
        {
            Console.WriteLine("[提示] 未检测到可用代理，将使用直连方式访问 GitHub。");
        }

        return _detectedProxy;
    }

    private static IWebProxy? TryGetProxy(IWebProxy? proxy, string successMessage)
    {
        if (proxy == null)
        {
            return null;
        }

        try
        {
            var proxyUri = proxy.GetProxy(GithubApiUri);
            if (proxyUri != null && proxyUri != GithubApiUri)
            {
                Console.WriteLine($"{successMessage}: {proxyUri}");
                return proxy;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] 检测代理时发生异常: {ex.Message}");
        }

        return null;
    }

    private static IWebProxy? TryGetProxyFromEnvironment()
    {
        string? httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy");
        string? httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy");
        string? proxyUrl = httpsProxy ?? httpProxy;

        if (string.IsNullOrWhiteSpace(proxyUrl))
        {
            return null;
        }

        try
        {
            var proxy = new WebProxy(proxyUrl);
            Console.WriteLine($"[提示] 从环境变量中检测到代理: {proxyUrl}");
            return proxy;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] 环境变量中的代理地址无效: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 为 HttpClient 创建带代理的处理程序。
    /// </summary>
    public static HttpClientHandler CreateHttpClientHandlerWithProxy()
    {
        return new HttpClientHandler
        {
            UseProxy = true,
            Proxy = DetectSystemProxy(),
            AllowAutoRedirect = true,
            UseDefaultCredentials = true
        };
    }

    /// <summary>
    /// 获取当前检测到的 HTTP 代理地址（用于 Git）。
    /// </summary>
    public static string? GetHttpProxyUrl()
    {
        try
        {
            var proxy = DetectSystemProxy();
            if (proxy != null)
            {
                var proxyUri = proxy.GetProxy(GithubApiUri);
                if (proxyUri != null && proxyUri != GithubApiUri)
                {
                    return proxyUri.ToString();
                }
            }

            return Environment.GetEnvironmentVariable("HTTPS_PROXY")
                ?? Environment.GetEnvironmentVariable("https_proxy")
                ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
                ?? Environment.GetEnvironmentVariable("http_proxy");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 读取 no_proxy / NO_PROXY 变量。
    /// </summary>
    public static string[] GetNoProxyHosts()
    {
        string? noProxy = Environment.GetEnvironmentVariable("NO_PROXY")
                         ?? Environment.GetEnvironmentVariable("no_proxy");

        if (string.IsNullOrEmpty(noProxy))
        {
            return Array.Empty<string>();
        }

        return noProxy.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// 打印当前代理相关信息，便于排查。
    /// </summary>
    public static void DisplayProxyInfo()
    {
        Console.WriteLine("========== 代理信息 ==========");

        var proxy = DetectSystemProxy();
        if (proxy != null)
        {
            var proxyUri = proxy.GetProxy(GithubApiUri);
            if (proxyUri != null && proxyUri != GithubApiUri)
            {
                Console.WriteLine($"GitHub API 使用代理: {proxyUri}");
            }
            else
            {
                Console.WriteLine("GitHub API 当前未使用代理。");
            }
        }
        else
        {
            Console.WriteLine("未检测到系统代理。");
        }

        string[] envVars = { "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "no_proxy" };
        foreach (var envVar in envVars)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
            {
                Console.WriteLine($"{envVar}={value}");
            }
        }

        Console.WriteLine("=================================");
    }
}
