using BTCPayTranslator.Models;
using BTCPayTranslator.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BTCPayTranslator.Tests.Services;

public class TranslationOrchestratorTests
{
    [Fact]
    public async Task GetSourceTranslationsAsync_UsesBTCPayEndpoint_WhenConfigured()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var handler = new QueueHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"hello\":\"Hello\"}")
            });

            var extractor = new TranslationExtractor(
                NullLogger<TranslationExtractor>.Instance,
                new HttpClient(handler));

            var orchestrator = CreateOrchestrator(
                extractor,
                new FileWriter(NullLogger<FileWriter>.Instance),
                new FakeTranslationService(),
                new Dictionary<string, string?>
                {
                    ["Translation:BTCPayUrl"] = "https://btcpay.test",
                    ["Translation:OutputDirectory"] = tempDir
                });

            var source = await orchestrator.GetSourceTranslationsAsync();

            Assert.Single(source);
            Assert.Equal("Hello", source["hello"]);
            Assert.Equal("https://btcpay.test/cheat/translations/default-en", handler.LastRequestUri?.ToString());
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
    public async Task TranslateToLanguageAsync_ReturnsFalse_ForUnsupportedLanguage()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var orchestrator = CreateOrchestrator(
                extractor,
                new FileWriter(NullLogger<FileWriter>.Instance),
                new FakeTranslationService(),
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile
                });

            var success = await orchestrator.TranslateToLanguageAsync("xx");

            Assert.False(success);
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
    public async Task TranslateToLanguageAsync_CreatesOutputFile_WithMergedTranslations()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fakeService = new FakeTranslationService(r => new TranslationResponse(r.Key, $"fr-{r.SourceText}", true));
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);
            var language = SupportedLanguages.GetLanguageInfo("fr")!;
            var outputPath = Path.Combine(tempDir, $"{language.Name.ToLower()}.json");

            await fileWriter.WriteBackendTranslationFileAsync(
                outputPath,
                language,
                new Dictionary<string, string> { ["existing"] = "value" });

            var orchestrator = CreateOrchestrator(
                extractor,
                fileWriter,
                fakeService,
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile,
                    ["Translation:BatchSize"] = "10",
                    ["Translation:DelayBetweenRequests"] = "0"
                });

            var success = await orchestrator.TranslateToLanguageAsync("fr");

            Assert.True(success);
            Assert.Equal(2, fakeService.SeenRequests.Count);

            var written = await fileWriter.LoadExistingBackendTranslationsAsync(outputPath);
            Assert.Equal(3, written.Count);
            Assert.Equal("value", written["existing"]);
            Assert.Equal("fr-Hello", written["hello"]);
            Assert.Equal("fr-bye", written["bye"]);
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
    public async Task TranslateToLanguageAsync_ReturnsTrue_WhenNoNewKeys()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fakeService = new FakeTranslationService();
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);
            var language = SupportedLanguages.GetLanguageInfo("fr")!;
            var outputPath = Path.Combine(tempDir, $"{language.Name.ToLower()}.json");

            await fileWriter.WriteBackendTranslationFileAsync(
                outputPath,
                language,
                new Dictionary<string, string>
                {
                    ["hello"] = "bonjour",
                    ["bye"] = "au revoir"
                });

            var orchestrator = CreateOrchestrator(
                extractor,
                fileWriter,
                fakeService,
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile
                });

            var success = await orchestrator.TranslateToLanguageAsync("fr");

            Assert.True(success);
            Assert.Empty(fakeService.SeenRequests);
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
    public async Task UpdateLanguageAsync_AddsNewAndRemovesDeletedKeys()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fakeService = new FakeTranslationService(r => new TranslationResponse(r.Key, $"upd-{r.SourceText}", true));
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);
            var language = SupportedLanguages.GetLanguageInfo("fr")!;
            var outputPath = Path.Combine(tempDir, $"{language.Name.ToLower()}.json");

            await fileWriter.WriteBackendTranslationFileAsync(
                outputPath,
                language,
                new Dictionary<string, string>
                {
                    ["hello"] = "bonjour",
                    ["obsolete"] = "obsolète"
                });

            var orchestrator = CreateOrchestrator(
                extractor,
                fileWriter,
                fakeService,
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile,
                    ["Translation:BatchSize"] = "10",
                    ["Translation:DelayBetweenRequests"] = "0"
                });

            var success = await orchestrator.UpdateLanguageAsync("fr");

            Assert.True(success);
            Assert.Single(fakeService.SeenRequests);
            Assert.Equal("bye", fakeService.SeenRequests[0].Key);

            var written = await fileWriter.LoadExistingBackendTranslationsAsync(outputPath);
            Assert.Equal(2, written.Count);
            Assert.Equal("bonjour", written["hello"]);
            Assert.Equal("upd-bye", written["bye"]);
            Assert.False(written.ContainsKey("obsolete"));
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
    public async Task UpdateAllLanguagesAsync_UpdatesOnlyKnownLanguageFiles()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fakeService = new FakeTranslationService(r => new TranslationResponse(r.Key, $"all-{r.SourceText}", true));
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);

            await fileWriter.WriteBackendTranslationFileAsync(
                Path.Combine(tempDir, "french.json"),
                SupportedLanguages.GetLanguageInfo("fr")!,
                new Dictionary<string, string> { ["hello"] = "bonjour" });

            await File.WriteAllTextAsync(Path.Combine(tempDir, "unknown.json"), "{\"test\":\"test\"}");

            var orchestrator = CreateOrchestrator(
                extractor,
                fileWriter,
                fakeService,
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile,
                    ["Translation:BatchSize"] = "10",
                    ["Translation:DelayBetweenRequests"] = "0"
                });

            var results = await orchestrator.UpdateAllLanguagesAsync();

            Assert.Single(results);
            Assert.True(results["fr"]);
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
    public async Task RefreshKeysAsync_AddsMissingKeys_FromLocalSource_WithoutTranslating()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);

            await fileWriter.WriteBackendTranslationFileAsync(
                Path.Combine(tempDir, "french.json"),
                SupportedLanguages.GetLanguageInfo("fr")!,
                new Dictionary<string, string> { ["hello"] = "bonjour" });

            var orchestrator = CreateOrchestrator(
                extractor,
                fileWriter,
                new FakeTranslationService(),
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile
                });

            var result = await orchestrator.RefreshKeysAsync();

            Assert.Equal(1, result.FilesProcessed);
            Assert.Equal(1, result.TotalKeysAdded);
            Assert.Equal(1, result.AddedByFile["french.json"]);

            var written = await fileWriter.LoadExistingBackendTranslationsAsync(Path.Combine(tempDir, "french.json"));
            Assert.Equal("bonjour", written["hello"]); // existing translation untouched
            Assert.Equal("bye", written["bye"]);        // new key inserted as English placeholder
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
    public async Task RefreshKeysAsync_SkipsUnknownLanguageFiles()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);

            await fileWriter.WriteBackendTranslationFileAsync(
                Path.Combine(tempDir, "french.json"),
                SupportedLanguages.GetLanguageInfo("fr")!,
                new Dictionary<string, string> { ["hello"] = "bonjour" });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "unknown.json"), "{\r\n  \"test\": \"test\"\r\n}");

            var orchestrator = CreateOrchestrator(
                extractor,
                fileWriter,
                new FakeTranslationService(),
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile
                });

            var result = await orchestrator.RefreshKeysAsync();

            Assert.Equal(1, result.FilesProcessed);
            Assert.Equal(1, result.FilesSkipped);
            Assert.True(result.AddedByFile.ContainsKey("french.json"));
            Assert.False(result.AddedByFile.ContainsKey("unknown.json"));
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
    public async Task RefreshKeysAsync_RespectsLanguageFilter()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);

            await fileWriter.WriteBackendTranslationFileAsync(
                Path.Combine(tempDir, "french.json"),
                SupportedLanguages.GetLanguageInfo("fr")!,
                new Dictionary<string, string> { ["hello"] = "bonjour" });
            await fileWriter.WriteBackendTranslationFileAsync(
                Path.Combine(tempDir, "german.json"),
                SupportedLanguages.GetLanguageInfo("de")!,
                new Dictionary<string, string> { ["hello"] = "hallo" });

            var orchestrator = CreateOrchestrator(
                extractor,
                fileWriter,
                new FakeTranslationService(),
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile
                });

            var result = await orchestrator.RefreshKeysAsync(new[] { "fr" });

            Assert.Equal(1, result.FilesProcessed);
            Assert.True(result.AddedByFile.ContainsKey("french.json"));
            Assert.False(result.AddedByFile.ContainsKey("german.json"));

            var german = await fileWriter.LoadExistingBackendTranslationsAsync(Path.Combine(tempDir, "german.json"));
            Assert.False(german.ContainsKey("bye")); // untouched
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
    public async Task RefreshKeysAsync_IsIdempotent_SecondRunAddsZero()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);

            await fileWriter.WriteBackendTranslationFileAsync(
                Path.Combine(tempDir, "french.json"),
                SupportedLanguages.GetLanguageInfo("fr")!,
                new Dictionary<string, string> { ["hello"] = "bonjour" });

            var orchestrator = CreateOrchestrator(
                extractor,
                fileWriter,
                new FakeTranslationService(),
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile
                });

            var first = await orchestrator.RefreshKeysAsync();
            var second = await orchestrator.RefreshKeysAsync();

            Assert.Equal(1, first.TotalKeysAdded);
            Assert.Equal(0, second.TotalKeysAdded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static TranslationOrchestrator CreateOrchestrator(
        TranslationExtractor extractor,
        FileWriter fileWriter,
        ITranslationService translationService,
        Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new TranslationOrchestrator(
            translationService,
            extractor,
            fileWriter,
            configuration,
            NullLogger<TranslationOrchestrator>.Instance);
    }

    private static (TranslationExtractor, string) CreateExtractorFromKnownTranslationsFile(string baseDirectory)
    {
        var filePath = Path.Combine(baseDirectory, "Translations.Default.cs");

        var content = "public class Seed\n" +
                      "{\n" +
                      "    public void Load()\n" +
                      "    {\n" +
                      "        var knownTranslations = \"\"\"\n" +
                      "{\n" +
                      "  \"hello\": \"Hello\",\n" +
                      "  \"bye\": \"\"\n" +
                      "}\n" +
                      "\"\"\";\n" +
                      "    }\n" +
                      "}\n";

        File.WriteAllText(filePath, content);

        var extractor = new TranslationExtractor(NullLogger<TranslationExtractor>.Instance, new HttpClient());
        return (extractor, filePath);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
