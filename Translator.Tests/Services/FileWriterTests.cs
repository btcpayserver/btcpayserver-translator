using BTCPayTranslator.Models;
using BTCPayTranslator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayTranslator.Tests.Services;

public class FileWriterTests
{
    [Fact]
    public async Task WriteBackendTranslationFileAsync_WritesSortedJson()
    {
        var sut = new FileWriter(NullLogger<FileWriter>.Instance);
        var language = SupportedLanguages.GetLanguageInfo("fr")!;
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "french.json");

        try
        {
            var translations = new Dictionary<string, string>
            {
                ["z"] = "Z",
                ["a"] = "A"
            };

            await sut.WriteBackendTranslationFileAsync(outputPath, language, translations);

            Assert.True(File.Exists(outputPath));
            var content = await File.ReadAllTextAsync(outputPath);
            var json = JObject.Parse(content);
            var keys = json.Properties().Select(p => p.Name).ToList();

            Assert.Equal(new[] { "a", "z" }, keys);
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
    public async Task LoadExistingBackendTranslationsAsync_Throws_OnMissingFile()
    {
        var sut = new FileWriter(NullLogger<FileWriter>.Instance);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            sut.LoadExistingBackendTranslationsAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
    }

    [Fact]
    public async Task LoadExistingBackendTranslationsAsync_SkipsEmptyValues()
    {
        var sut = new FileWriter(NullLogger<FileWriter>.Instance);
        var tempFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempFile, """
                {
                  "hello": "bonjour",
                  "empty": ""
                }
                """);

            var result = await sut.LoadExistingBackendTranslationsAsync(tempFile);

            Assert.Single(result);
            Assert.Equal("bonjour", result["hello"]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task WriteSummaryReportAsync_WritesReportFile()
    {
        var sut = new FileWriter(NullLogger<FileWriter>.Instance);
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "french.json");

        try
        {
            var response = new BatchTranslationResponse(
                new List<TranslationResponse>
                {
                    new("k1", "v1", true),
                    new("k2", "v2", false, "failed")
                },
                SuccessCount: 1,
                FailureCount: 1,
                Duration: TimeSpan.FromSeconds(1));

            await sut.WriteSummaryReportAsync(outputPath, "French", response, new Dictionary<string, string> { ["k1"] = "v1" });

            var reportPath = Path.ChangeExtension(outputPath, ".report.json");
            Assert.True(File.Exists(reportPath));

            var content = await File.ReadAllTextAsync(reportPath);
            var report = JObject.Parse(content);
            Assert.Equal("French", report["Language"]?.Value<string>());
            Assert.Equal(1, report["Translation"]?["SuccessfulTranslations"]?.Value<int>());
            Assert.Equal(1, report["Translation"]?["FailedTranslations"]?.Value<int>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
