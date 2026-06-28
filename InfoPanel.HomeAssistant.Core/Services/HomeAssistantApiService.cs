using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

public sealed class HomeAssistantApiService : IDisposable
{
    private readonly HttpClient _httpClient;
    private string _baseUrl = string.Empty;
    private string _accessToken = string.Empty;

    public HomeAssistantApiService()
    {
        _httpClient = new HttpClient(CreateHandler())
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_baseUrl) &&
        !string.IsNullOrWhiteSpace(_accessToken);

    public void Configure(string baseUrl, string accessToken)
    {
        _baseUrl = baseUrl.Trim().TrimEnd('/');
        _accessToken = accessToken.Trim();

        _httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(_accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    public async Task<IReadOnlyList<HomeAssistantEntityState>> GetAllStatesAsync(
        CancellationToken cancellationToken)
    {
        string url = $"{_baseUrl}/api/states";
        string json = await GetStringAsync(url, cancellationToken);

        var states = JsonSerializer.Deserialize<List<HomeAssistantEntityState>>(json) ?? [];
        return states
            .Where(s => !string.IsNullOrWhiteSpace(s.EntityId))
            .GroupBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Fetches current states for the given entity ids via individual REST calls.
    /// </summary>
    /// <param name="entityIds">Entity ids to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<HomeAssistantEntityState>> GetEntityStatesAsync(
        IEnumerable<string> entityIds,
        CancellationToken cancellationToken)
    {
        var ids = entityIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        using var gate = new SemaphoreSlim(8);
        var tasks = ids.Select(async entityId =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await GetEntityStateAsync(entityId, cancellationToken);
            }
            catch
            {
                return null;
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results
            .Where(state => state != null)
            .Cast<HomeAssistantEntityState>()
            .OrderBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<HomeAssistantEntityState> GetEntityStateAsync(
        string entityId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new InvalidOperationException("Entity ID is not configured.");
        }

        string url = $"{_baseUrl}/api/states/{entityId}";
        string json = await GetStringAsync(url, cancellationToken);

        return JsonSerializer.Deserialize<HomeAssistantEntityState>(json)
            ?? throw new InvalidOperationException("Empty response from Home Assistant.");
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Home Assistant API is not configured.");
        }

        try
        {
            return await _httpClient.GetStringAsync(url, cancellationToken);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && IsSocketAccessDenied(ex))
        {
            return await GetStringViaCurlAsync(url, cancellationToken);
        }
    }

    private static bool IsSocketAccessDenied(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.AccessDenied })
            {
                return true;
            }

            if (current.Message.Contains(
                    "access a socket in a way forbidden by its access permissions",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string> GetStringViaCurlAsync(string url, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "curl.exe",
            Arguments = $"-sS -H \"Authorization: Bearer {_accessToken}\" \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start curl.exe.");

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? $"curl.exe failed with exit code {process.ExitCode}."
                    : stderr.Trim());
        }

        return stdout;
    }

    private static HttpMessageHandler CreateHandler()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return new WinHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    WindowsProxyUsePolicy = WindowsProxyUsePolicy.DoNotUseProxy,
                };
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        return new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
    }
}
