using System.Security.Cryptography;
using System.Text.Json;
using BTCPayTranslator.Models;
using BTCPayTranslator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BTCPayTranslator.Tests.Services;

public class ManifestGeneratorTests
{
    [Fact]
    public async Task GenerateManifest_WritesManifest_ForValidTranslationFile()
    {
        var tempDir = CreateTempDirectory();
        var translationsDir = Path.Combine(tempDir, "translations");
        Directory.CreateDirectory(translationsDir);
        var translationFile = Path.Combine(translationsDir, "French.json");
        var manifestPath = Path.Combine(tempDir, "manifest.json");

        try
        {
            await File.WriteAllTextAsync(translationFile, """
                {
                  "_maintainer": "alice|https://github.com/alice",
                  "hello": "bonjour"
                }
                """);

            var sut = CreateSut();
            var result = await sut.GenerateManifest(translationsDir, manifestPath);

            Assert.True(result);
            Assert.True(File.Exists(manifestPath));

            var manifest = await ReadManifest(manifestPath);
            var entry = Assert.Single(manifest.Languages);

            Assert.Equal("fr", entry.Code);
            Assert.Equal("fr-FR", entry.Bcp47);
            Assert.Equal("French", entry.Name);
            Assert.Equal("Français", entry.Native);
            Assert.Equal("translations/French.json", entry.File);
            Assert.Equal("alice|https://github.com/alice", entry.Maintainer);
            Assert.Equal(ComputeSha256(translationFile), entry.Sha);
            Assert.Matches("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z$", entry.Updated);
            Assert.False(entry.Rtl);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateManifest_SetsRtlTrue_ForRightToLeftLanguage()
    {
        var tempDir = CreateTempDirectory();
        var translationsDir = Path.Combine(tempDir, "translations");
        Directory.CreateDirectory(translationsDir);
        var translationFile = Path.Combine(translationsDir, "Arabic.json");
        var manifestPath = Path.Combine(tempDir, "manifest.json");

        try
        {
            await File.WriteAllTextAsync(translationFile, """
                {
                  "_maintainer": "omar|https://github.com/omar",
                  "hello": "مرحبا"
                }
                """);

            var sut = CreateSut();
            var result = await sut.GenerateManifest(translationsDir, manifestPath);

            Assert.True(result);
            var manifest = await ReadManifest(manifestPath);
            var entry = Assert.Single(manifest.Languages);

            Assert.Equal("ar", entry.Code);
            Assert.Equal("Arabic", entry.Name);
            Assert.True(entry.Rtl);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateManifest_ReturnsFalse_WhenNoTranslationFilesExist()
    {
        var tempDir = CreateTempDirectory();
        var manifestPath = Path.Combine(tempDir, "manifest.json");

        try
        {
            var sut = CreateSut();

            var result = await sut.GenerateManifest(tempDir, manifestPath);

            Assert.False(result);
            Assert.False(File.Exists(manifestPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateManifest_ReturnsFalse_WhenTranslationDirectoryDoesNotExist()
    {
        var translationsDir = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.Tests", Guid.NewGuid().ToString("N"));
        var manifestPath = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.Tests", Guid.NewGuid().ToString("N"), "manifest.json");
        var sut = CreateSut();

        var result = await sut.GenerateManifest(translationsDir, manifestPath);

        Assert.False(result);
    }

    [Fact]
    public async Task GenerateManifest_RetainsUpdated_WhenExistingShaMatches()
    {
        var tempDir = CreateTempDirectory();
        var translationsDir = Path.Combine(tempDir, "translations");
        Directory.CreateDirectory(translationsDir);
        var translationFile = Path.Combine(translationsDir, "French.json");
        var manifestPath = Path.Combine(tempDir, "manifest.json");

        try
        {
            await File.WriteAllTextAsync(translationFile, """
                {
                  "_maintainer": "alice|https://github.com/alice",
                  "hello": "bonjour"
                }
                """);

            var existingSha = ComputeSha256(translationFile);
            var expectedUpdated = "2024-01-02T03:04:05Z";
            var existingManifest = new Manifest(
                new List<ManifestEntry>
                {
                    new(
                        Code: "fr",
                        Bcp47: "fr-FR",
                        Name: "French",
                        Native: "Français",
                        Rtl: false,
                        File: "translations/French.json",
                        Sha: existingSha,
                        Maintainer: "old",
                        Updated: expectedUpdated)
                },
                Redirect: null);

            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(existingManifest));

            var sut = CreateSut();
            var result = await sut.GenerateManifest(translationsDir, manifestPath);

            Assert.True(result);
            var generated = await ReadManifest(manifestPath);
            var entry = Assert.Single(generated.Languages);
            Assert.Equal(expectedUpdated, entry.Updated);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateManifest_UpdatesUpdated_WhenExistingShaDiffers()
    {
        var tempDir = CreateTempDirectory();
        var translationsDir = Path.Combine(tempDir, "translations");
        Directory.CreateDirectory(translationsDir);
        var translationFile = Path.Combine(translationsDir, "French.json");
        var manifestPath = Path.Combine(tempDir, "manifest.json");

        try
        {
            await File.WriteAllTextAsync(translationFile, """
                {
                  "_maintainer": "alice|https://github.com/alice",
                  "hello": "bonjour"
                }
                """);

            var previousUpdated = "2024-01-02T03:04:05Z";
            var existingManifest = new Manifest(
                new List<ManifestEntry>
                {
                    new(
                        Code: "fr",
                        Bcp47: "fr-FR",
                        Name: "French",
                        Native: "Français",
                        Rtl: false,
                        File: "translations/French.json",
                        Sha: "deadbeef",
                        Maintainer: "old",
                        Updated: previousUpdated)
                },
                Redirect: null);

            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(existingManifest));

            var sut = CreateSut();
            var result = await sut.GenerateManifest(translationsDir, manifestPath);

            Assert.True(result);
            var generated = await ReadManifest(manifestPath);
            var entry = Assert.Single(generated.Languages);

            Assert.NotEqual(previousUpdated, entry.Updated);
            Assert.Matches("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z$", entry.Updated);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateManifest_SetsMaintainerToNull_WhenFieldMissing()
    {
        var tempDir = CreateTempDirectory();
        var translationFile = Path.Combine(tempDir, "French.json");
        var manifestPath = Path.Combine(tempDir, "manifest.json");

        try
        {
            await File.WriteAllTextAsync(translationFile, """
                {
                  "hello": "bonjour"
                }
                """);

            var sut = CreateSut();
            var result = await sut.GenerateManifest(tempDir, manifestPath);

            Assert.True(result);
            var manifest = await ReadManifest(manifestPath);
            var entry = Assert.Single(manifest.Languages);
            Assert.Null(entry.Maintainer);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateManifest_ReturnsFalse_WhenLanguageNameMappingIsMissing()
    {
        var tempDir = CreateTempDirectory();
        var translationFile = Path.Combine(tempDir, "Klingon.json");
        var manifestPath = Path.Combine(tempDir, "manifest.json");

        try
        {
            await File.WriteAllTextAsync(translationFile, """
                {
                  "_maintainer": "alice|https://github.com/alice",
                  "hello": "nuqneH"
                }
                """);

            var sut = CreateSut();
            var result = await sut.GenerateManifest(tempDir, manifestPath);

            Assert.False(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateManifest_ReturnsFalse_WhenTranslationFileHasInvalidJson()
    {
        var tempDir = CreateTempDirectory();
        var translationFile = Path.Combine(tempDir, "French.json");
        var manifestPath = Path.Combine(tempDir, "manifest.json");

        try
        {
            await File.WriteAllTextAsync(translationFile, "{\"_maintainer\":");

            var sut = CreateSut();
            var result = await sut.GenerateManifest(tempDir, manifestPath);

            Assert.False(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static ManifestGenerator CreateSut()
    {
        return new ManifestGenerator(NullLogger<ManifestGenerator>.Instance);
    }

    private static async Task<Manifest> ReadManifest(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var manifest = JsonSerializer.Deserialize<Manifest>(json);
        return Assert.IsType<Manifest>(manifest);
    }

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
