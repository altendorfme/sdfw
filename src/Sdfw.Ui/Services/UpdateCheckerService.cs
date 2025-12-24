using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Sdfw.Ui.Services;

internal sealed class VersionFileModel
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;
}

public sealed class UpdateCheckerService : IUpdateCheckerService
{
    private const string VersionUrl = "https://raw.githubusercontent.com/altendorfme/sdfw/main/version.json";
    
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateCheckerService> _logger;
    private readonly string _currentVersion;

    public UpdateCheckerService(HttpClient httpClient, ILogger<UpdateCheckerService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        _currentVersion = version is not null 
            ? $"{version.Major}.{version.Minor}.{version.Build}" 
            : "1.0.0";
    }

    public string CurrentVersion => _currentVersion;

    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(VersionUrl, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var versionFile = JsonSerializer.Deserialize<VersionFileModel>(json);
            
            if (versionFile is null || string.IsNullOrEmpty(versionFile.Version))
            {
                return null;
            }

            var isUpdateAvailable = CompareVersions(versionFile.Version, _currentVersion) > 0;
            
            var updateInfo = new UpdateInfo
            {
                Version = versionFile.Version,
                DownloadUrl = versionFile.DownloadUrl,
                IsUpdateAvailable = isUpdateAvailable
            };

            return updateInfo;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int CompareVersions(string version1, string version2)
    {
        var v1Parts = version1.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var v2Parts = version2.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();

        var maxLength = Math.Max(v1Parts.Length, v2Parts.Length);

        for (var i = 0; i < maxLength; i++)
        {
            var v1Part = i < v1Parts.Length ? v1Parts[i] : 0;
            var v2Part = i < v2Parts.Length ? v2Parts[i] : 0;

            if (v1Part > v2Part) return 1;
            if (v1Part < v2Part) return -1;
        }

        return 0;
    }
}
