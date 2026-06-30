using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using BTCPayTranslator.Models;
using Microsoft.Extensions.Logging;

namespace BTCPayTranslator.Services;

public class ManifestGenerator
{
    private static string FormatUtcTimestamp(DateTime utc) =>
        utc.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private readonly ILogger<ManifestGenerator> _logger;

    public ManifestGenerator(ILogger<ManifestGenerator> logger)
    {
        _logger = logger;
    }

    private IEnumerable<string>? GetTranslationFiles(string translationDirectoryPath)
    {
        try
        {
            return Directory.GetFiles(translationDirectoryPath, "*.json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't find translation files in {Directory}", translationDirectoryPath);
            throw new Exception("Couldn't find translation files", ex);
        }
    }

    private async Task<string?> HashFiles(string filePath)
    {
        using var sha256 = SHA256.Create();
        try
        {
            await using var stream = File.OpenRead(filePath);
            var hashBytes = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hashBytes).ToLower();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hash file {FilePath}", filePath);
            throw new Exception("Couldn't hash translation file", ex);
        }
    }

    private async Task<string?> GetMaintainer(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("_maintainer", out var maintainer))
            {
                return maintainer.GetString();
            }

            _logger.LogWarning("Missing _maintainer field in {FilePath}", filePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read maintainer from {FilePath}", filePath);
            throw new Exception("Couldn't read maintainer from translation file", ex);
        }
    }

    private async Task<ManifestEntry> BuildEntry(string filePath, ManifestEntry? existingEntry, string runUpdatedAt)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            var result = SupportedLanguages.GetLanguageInfoByName(fileName);
            if (!result.HasValue)
            {
                _logger.LogError("No language info mapping found for translation file {FileName}", fileName);
                throw new Exception($"No language info found for {fileName}");
            }
            var (code, langInfo) = result.Value;

            var hashedFile = await HashFiles(filePath);
            if (hashedFile == null)
            {
                _logger.LogError("Skipping {FilePath} because hash generation failed", filePath);
                throw new Exception($"Hash generation failed for {filePath}");
            }

            var maintainer = await GetMaintainer(filePath);
            
            var updatedAt = existingEntry?.Sha == hashedFile ? existingEntry!.Updated : runUpdatedAt;

            var entry = new ManifestEntry(
                Code: code,
                Bcp47: langInfo.Code,
                Name: langInfo.Name,
                Native: langInfo.NativeName,
                Rtl: langInfo.IsRightToLeft,
                File: "translations/" + fileName + ".json",
                Sha: hashedFile,
                Maintainer: maintainer,
                Updated: updatedAt);

            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build manifest entry for {FilePath}", filePath);
            throw new Exception("Couldn't build manifest entry", ex);
        }
    }

    public async Task<bool> GenerateManifest(string translationDirectoryPath, string manifestOutputPath)
    {
        try
        {
            _logger.LogInformation("Starting manifest generation");

            var runUpdatedAt = FormatUtcTimestamp(DateTime.UtcNow);

            Manifest? existingManifest = null;
            if (File.Exists(manifestOutputPath))
            {
                var existingJson = await File.ReadAllTextAsync(manifestOutputPath);
                existingManifest = JsonSerializer.Deserialize<Manifest>(existingJson);
            }

            var files = GetTranslationFiles(translationDirectoryPath)?.OrderBy(f=> f).ToArray();
            if (files == null || files.Length == 0)
            {
                _logger.LogError("No translation files found to generate manifest");
                return false;
            }

            var entries = new List<ManifestEntry>();
            foreach (var file in files)
            {
                var existingEntry = existingManifest?.Languages
                    .FirstOrDefault(e => e.File == "translations/" + Path.GetFileName(file));

                var entry = await BuildEntry(file, existingEntry, runUpdatedAt);
                entries.Add(entry);
            }

            var manifest = new Manifest(entries, Redirect: null);

            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            await File.WriteAllTextAsync(manifestOutputPath, manifestJson);

            _logger.LogInformation("Manifest generated with {EntryCount}/{FileCount} entries at {ManifestPath}", entries.Count, files.Length, manifestOutputPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manifest generation failed");
            return false;
        }
    }
}
