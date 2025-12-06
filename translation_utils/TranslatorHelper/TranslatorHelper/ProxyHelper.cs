using System;
using System.Net;
using System.Net.Http;

/// <summary>
/// 代理检测和配置辅助类
/// 自动检测系统代理配置并应用到 HTTP 请求和 Git 操作
/// </summary>
public static class ProxyHelper
{
    private static IWebProxy? _detectedProxy;
    private static bool _proxyDetected = false;

    /// <summary>
    /// 检测系统代理配置
    /// 支持：HTTP/HTTPS 代理、系统代理设置、PAC 脚本
    /// 不支持：SOCKS5（需手动配置）、VPN、路由策略、特殊加速器
    /// </summary>
    public static IWebProxy? DetectSystemProxy()
    {
        if (_proxyDetected)
        {
            return _detectedProxy;
        }

        try
        {
            // 方法1: 使用 WebRequest.DefaultWebProxy (推荐，支持 PAC)
            var defaultProxy = WebRequest.DefaultWebProxy;
            if (defaultProxy != null)
            {
                // 测试代理是否对 GitHub 有效
                var testUri = new Uri("https://api.github.com");
                var proxyUri = defaultProxy.GetProxy(testUri);
                
                // 如果返回的代理 URI 与原 URI 不同，说明需要使用代理
                if (proxyUri != null && proxyUri != testUri)
                {
                    Console.WriteLine($"[代理] 检测到系统代理: {proxyUri}");
                    _detectedProxy = defaultProxy;
                    _proxyDetected = true;
                    return defaultProxy;
                }
            }

            // 方法2: 从环境变量检测代理
            string? httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY") 
                              ?? Environment.GetEnvironmentVariable("http_proxy");
            string? httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") 
                               ?? Environment.GetEnvironmentVariable("https_proxy");
            
            string? proxyUrl = httpsProxy ?? httpProxy;
            
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                try
                {
                    var proxy = new WebProxy(proxyUrl);
                    Console.WriteLine($"[代理] 从环境变量检测到代理: {proxyUrl}");
                    _detectedProxy = proxy;
                    _proxyDetected = true;
                    return proxy;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[代理] 环境变量代理格式无效: {ex.Message}");
                }
            }

            // 方法3: 从 IE/Windows 系统设置检测（WebRequest.DefaultWebProxy 已包含此逻辑）
            // 这里主要作为备用方案
            var systemProxy = WebRequest.GetSystemWebProxy();
            if (systemProxy != null)
            {
                var testUri = new Uri("https://api.github.com");
                var proxyUri = systemProxy.GetProxy(testUri);
                
                if (proxyUri != null && proxyUri != testUri)
                {
                    Console.WriteLine($"[代理] 检测到 Windows 系统代理: {proxyUri}");
                    _detectedProxy = systemProxy;
                    _proxyDetected = true;
                    return systemProxy;
                }
            }

            Console.WriteLine("[代理] 未检测到系统代理配置，将使用直连");
            _proxyDetected = true;
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[代理] 代理检测过程出现异常: {ex.Message}");
            _proxyDetected = true;
            return null;
        }
    }

    /// <summary>
    /// 为 HttpClient 配置代理
    /// </summary>
    public static HttpClientHandler CreateHttpClientHandlerWithProxy()
    {
        var handler = new HttpClientHandler
        {
            // 使用系统默认代理配置
            UseProxy = true,
            Proxy = DetectSystemProxy(),
            // 自动跟随重定向
            AllowAutoRedirect = true,
            // 使用默认凭据（用于需要身份验证的代理）
            UseDefaultCredentials = true
        };

        return handler;
    }

    /// <summary>
    /// 获取 HTTP 代理 URL (用于 Git 环境变量)
    /// </summary>
    public static string? GetHttpProxyUrl()
    {
        try
        {
            var systemProxy = DetectSystemProxy();
            
            if (systemProxy != null)
            {
                var testUri = new Uri("https://api.github.com");
                var proxyUri = systemProxy.GetProxy(testUri);
                
                if (proxyUri != null && proxyUri != testUri)
                {
                    return proxyUri.ToString();
                }
            }

            // Fallback to environment variables
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
    /// 从环境变量获取 no_proxy 配置
    /// </summary>
    public static string[] GetNoProxyHosts()
    {
        string? noProxy = Environment.GetEnvironmentVariable("NO_PROXY") 
                       ?? Environment.GetEnvironmentVariable("no_proxy");
        
        if (string.IsNullOrEmpty(noProxy))
        {
            return Array.Empty<string>();
        }

        return noProxy.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => s.Trim())
                     .ToArray();
    }

    /// <summary>
    /// 显示代理配置信息（用于调试）
    /// </summary>
    public static void DisplayProxyInfo()
    {
        Console.WriteLine("========== 代理配置信息 ==========");
        
        var proxy = DetectSystemProxy();
        if (proxy != null)
        {
            var testUri = new Uri("https://api.github.com");
            var proxyUri = proxy.GetProxy(testUri);
            Console.WriteLine($"GitHub API 使用代理: {proxyUri}");
        }
        else
        {
            Console.WriteLine("未检测到代理配置");
        }

        // 显示环境变量
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
