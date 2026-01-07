using System.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayTranslator.Services;

public class TranslationExtractor
{
    private readonly ILogger<TranslationExtractor> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public TranslationExtractor(ILogger<TranslationExtractor> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "BTCPayTranslator", "cache");
        
        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<Dictionary<string, string>> ExtractFromDefaultFileAsync(string filePathOrUrl)
    {
        try
        {
            string content;
            string sourceDescription;

            if (IsUrl(filePathOrUrl))
            {
                content = await DownloadFileContentAsync(filePathOrUrl);
                sourceDescription = $"URL: {filePathOrUrl}";
            }
            else
            {
                if (!File.Exists(filePathOrUrl))
                {
                    throw new FileNotFoundException($"Translation file not found: {filePathOrUrl}");
                }
                content = await File.ReadAllTextAsync(filePathOrUrl);
                sourceDescription = $"File: {filePathOrUrl}";
            }
            
            // Extract the JSON content from the C# file
            var jsonMatch = Regex.Match(content, @"var knownTranslations =\s*""\""\""\s*\n(.*?)\n""\""\"";", RegexOptions.Singleline);
            
            if (!jsonMatch.Success)
            {
                throw new InvalidOperationException("Could not find knownTranslations JSON in the file");
            }

            var jsonContent = jsonMatch.Groups[1].Value;
            
            // Parse the JSON content
            var translations = new Dictionary<string, string>();
            var jsonObject = JObject.Parse(jsonContent);

            foreach (var property in jsonObject.Properties())
            {
                var key = property.Name;
                var value = property.Value?.ToString() ?? "";
                
                // Skip empty translations (they default to the key itself)
                if (!string.IsNullOrEmpty(value))
                {
                    translations[key] = value;
                }
                else
                {
                    translations[key] = key; // Use key as default value
                }
            }

            _logger.LogInformation("Extracted {Count} translations from {Source}", translations.Count, sourceDescription);
            return translations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting translations from {Source}", filePathOrUrl);
            throw;
        }
    }

    public async Task<Dictionary<string, string>> LoadExistingTranslationsAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogInformation("Existing translation file not found: {FilePath}", filePath);
                return new Dictionary<string, string>();
            }

            var content = await File.ReadAllTextAsync(filePath);
            var jsonObject = JObject.Parse(content);
            var translations = new Dictionary<string, string>();

            foreach (var property in jsonObject.Properties())
            {
                // Skip metadata fields
                if (property.Name is "NOTICE_WARN" or "code" or "currentLanguage")
                    continue;

                var value = property.Value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(value))
                {
                    translations[property.Name] = value;
                }
            }

            _logger.LogInformation("Loaded {Count} existing translations from {FilePath}", translations.Count, filePath);
            return translations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading existing translations from {FilePath}", filePath);
            return new Dictionary<string, string>();
        }
    }

    public Dictionary<string, string> GetTranslationsToUpdate(
        Dictionary<string, string> sourceTranslations,
        Dictionary<string, string> existingTranslations)
    {
        var toUpdate = new Dictionary<string, string>();

        foreach (var source in sourceTranslations)
        {
            // Add if not exists or if source has changed significantly
            if (!existingTranslations.ContainsKey(source.Key))
            {
                toUpdate[source.Key] = source.Value;
            }
            // Optionally re-translate if source text changed (implement hashing if needed)
        }

        _logger.LogInformation("Found {Count} translations to update", toUpdate.Count);
        return toUpdate;
    }

    public Dictionary<string, string> MergeTranslations(
        Dictionary<string, string> existingTranslations,
        Dictionary<string, string> newTranslations)
    {
        var merged = new Dictionary<string, string>(existingTranslations);

        foreach (var newTranslation in newTranslations)
        {
            merged[newTranslation.Key] = newTranslation.Value;
        }

        _logger.LogInformation("Merged translations: {ExistingCount} existing + {NewCount} new = {TotalCount} total",
            existingTranslations.Count, newTranslations.Count, merged.Count);

        return merged;
    }

    private bool IsUrl(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && 
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private async Task<string> DownloadFileContentAsync(string url)
    {
        try
        {
            // Check cache first
            var cacheKey = GetCacheKey(url);
            var cachePath = Path.Combine(_cacheDirectory, cacheKey);
            
            if (File.Exists(cachePath))
            {
                var cacheAge = DateTime.Now - File.GetLastWriteTime(cachePath);
                // Use cache if it's less than 1 hour old
                if (cacheAge.TotalHours < 1)
                {
                    _logger.LogInformation("Using cached file for {Url}", url);
                    return await File.ReadAllTextAsync(cachePath);
                }
            }

            _logger.LogInformation("Downloading file from {Url}", url);
            
            // Convert GitHub blob URL to raw URL if needed
            var downloadUrl = ConvertToRawUrl(url);
            
            var response = await _httpClient.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            
            // Cache the content
            await File.WriteAllTextAsync(cachePath, content);
            _logger.LogInformation("Cached downloaded content to {CachePath}", cachePath);
            
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from {Url}", url);
            throw;
        }
    }

    private string GetCacheKey(string url)
    {
        // Create a safe filename from the URL
        var uri = new Uri(url);
        var filename = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrEmpty(filename))
        {
            filename = "default_translations.cs";
        }
        
        // Add a hash of the URL to make it unique
        var urlHash = url.GetHashCode().ToString("X");
        return $"{Path.GetFileNameWithoutExtension(filename)}_{urlHash}.cs";
    }

    private string ConvertToRawUrl(string url)
    {
        // Convert GitHub blob URL to raw URL
        if (url.Contains("github.com") && url.Contains("/blob/"))
        {
            return url.Replace("/blob/", "/raw/");
        }
        return url;
    }

    public async Task<Dictionary<string, string>> ExtractFromCheckoutJsonAsync(string filePathOrUrl)
    {
        try
        {
            string content;
            string sourceDescription;

            if (IsUrl(filePathOrUrl))
            {
                content = await DownloadCheckoutFileContentAsync(filePathOrUrl);
                sourceDescription = $"URL: {filePathOrUrl}";
            }
            else
            {
                if (!File.Exists(filePathOrUrl))
                {
                    throw new FileNotFoundException($"Checkout translation file not found: {filePathOrUrl}");
                }
                content = await File.ReadAllTextAsync(filePathOrUrl);
                sourceDescription = $"File: {filePathOrUrl}";
            }
            
            // Parse the JSON content
            var translations = new Dictionary<string, string>();
            var jsonObject = JObject.Parse(content);

            foreach (var property in jsonObject.Properties())
            {
                // Skip metadata fields
                if (property.Name is "NOTICE_WARN" or "code" or "currentLanguage")
                    continue;

                var key = property.Name;
                var value = property.Value?.ToString() ?? "";
                
                // Include all translation keys
                if (!string.IsNullOrEmpty(value))
                {
                    translations[key] = value;
                }
            }

            _logger.LogInformation("Extracted {Count} checkout translations from {Source}", translations.Count, sourceDescription);
            return translations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting checkout translations from {Source}", filePathOrUrl);
            throw;
        }
    }

    private async Task<string> DownloadCheckoutFileContentAsync(string url)
    {
        try
        {
            // Check cache first
            var cacheKey = GetCheckoutCacheKey(url);
            var cachePath = Path.Combine(_cacheDirectory, cacheKey);
            
            if (File.Exists(cachePath))
            {
                var cacheAge = DateTime.Now - File.GetLastWriteTime(cachePath);
                // Use cache if it's less than 1 hour old
                if (cacheAge.TotalHours < 1)
                {
                    _logger.LogInformation("Using cached checkout file for {Url}", url);
                    return await File.ReadAllTextAsync(cachePath);
                }
            }

            _logger.LogInformation("Downloading checkout file from {Url}", url);
            
            // Convert GitHub blob URL to raw URL if needed
            var downloadUrl = ConvertToRawUrl(url);
            
            var response = await _httpClient.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            
            // Cache the content
            await File.WriteAllTextAsync(cachePath, content);
            _logger.LogInformation("Cached downloaded checkout content to {CachePath}", cachePath);
            
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading checkout file from {Url}", url);
            throw;
        }
    }

    private string GetCheckoutCacheKey(string url)
    {
        // Create a safe filename from the URL
        var uri = new Uri(url);
        var filename = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrEmpty(filename))
        {
            filename = "checkout_en.json";
        }
        
        // Add a hash of the URL to make it unique
        var urlHash = url.GetHashCode().ToString("X");
        return $"{Path.GetFileNameWithoutExtension(filename)}_{urlHash}.json";
    }
}
