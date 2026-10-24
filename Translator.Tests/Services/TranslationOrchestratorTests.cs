using BTCPayTranslator.Models;
using BTCPayTranslator.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
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
                    ["Translation:DelayBetweenRequests"] = "0",
                    ["Translation:MaxPruneRatio"] = "0.5"
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

    private const string MetadataPack = """
        {
          "_maintainer": "Reviewer|https://example.org/reviewer",
          "_source": "https://example.org/source",
          "bye": "au revoir",
          "hello": "bonjour"
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_PreservesMetadataAndTranslations_WhenSourceCoversAllKeys(bool updateAll)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fakeService = new FakeTranslationService();
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);
            var outputPath = Path.Combine(tempDir, "french.json");
            await File.WriteAllTextAsync(outputPath, MetadataPack);

            var orchestrator = CreateOrchestrator(extractor, fileWriter, fakeService,
                UpdateSettings(tempDir, inputFile));

            var success = await RunUpdateAsync(orchestrator, updateAll);

            Assert.True(success);
            Assert.Empty(fakeService.SeenRequests);
            var keys = ReadKeys(outputPath);
            Assert.Equal(new[] { "_maintainer", "_source", "bye", "hello" }, keys);
            var written = await fileWriter.LoadExistingBackendTranslationsAsync(outputPath);
            Assert.Equal("Reviewer|https://example.org/reviewer", written["_maintainer"]);
            Assert.Equal("https://example.org/source", written["_source"]);
            Assert.Equal("bonjour", written["hello"]);
            Assert.Equal("au revoir", written["bye"]);

            var manifestPath = Path.Combine(tempDir, "manifest.json");
            Assert.True(await new ManifestGenerator(NullLogger<ManifestGenerator>.Instance)
                .GenerateManifest(tempDir, manifestPath));
            Assert.Contains("Reviewer|https://example.org/reviewer", await File.ReadAllTextAsync(manifestPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_PreservesMetadata_WhenSourceIsRuntimeEndpoint(bool updateAll)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var handler = new QueueHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"hello\":\"Hello\",\"bye\":\"Bye\"}")
            });
            var extractor = new TranslationExtractor(NullLogger<TranslationExtractor>.Instance, new HttpClient(handler));
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);
            var outputPath = Path.Combine(tempDir, "french.json");
            await File.WriteAllTextAsync(outputPath, MetadataPack);

            var orchestrator = CreateOrchestrator(extractor, fileWriter, new FakeTranslationService(),
                new Dictionary<string, string?>
                {
                    ["Translation:BTCPayUrl"] = "https://btcpay.test",
                    ["Translation:OutputDirectory"] = tempDir
                });

            var success = await RunUpdateAsync(orchestrator, updateAll);

            Assert.True(success);
            Assert.Equal(new[] { "_maintainer", "_source", "bye", "hello" }, ReadKeys(outputPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_AddsNewKey_AndPreservesMetadata(bool updateAll)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fakeService = new FakeTranslationService(r => new TranslationResponse(r.Key, $"upd-{r.SourceText}", true));
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);
            var outputPath = Path.Combine(tempDir, "french.json");
            await File.WriteAllTextAsync(outputPath, """
                {
                  "_maintainer": "Reviewer|https://example.org/reviewer",
                  "hello": "bonjour"
                }
                """);

            var orchestrator = CreateOrchestrator(extractor, fileWriter, fakeService,
                UpdateSettings(tempDir, inputFile));

            var success = await RunUpdateAsync(orchestrator, updateAll);

            Assert.True(success);
            Assert.Equal("bye", Assert.Single(fakeService.SeenRequests).Key);
            Assert.Equal(new[] { "_maintainer", "bye", "hello" }, ReadKeys(outputPath));
            var written = await fileWriter.LoadExistingBackendTranslationsAsync(outputPath);
            Assert.Equal("upd-bye", written["bye"]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_PrunesObsoleteKeyWithinDefaultThreshold_AndPreservesMetadata(bool updateAll)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var sourceKeys = Enumerable.Range(0, 10).Select(i => $"key{i}").ToList();
            var (extractor, inputFile) = CreateExtractorFromSourceJson(tempDir,
                "{" + string.Join(",", sourceKeys.Select(k => $"\"{k}\":\"\"")) + "}");
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);
            var outputPath = Path.Combine(tempDir, "french.json");
            var pack = new JObject { ["_maintainer"] = "Reviewer|https://example.org/reviewer" };
            foreach (var key in sourceKeys)
            {
                pack[key] = $"fr-{key}";
            }
            pack["obsolete"] = "obsolète";
            await File.WriteAllTextAsync(outputPath, pack.ToString());

            var orchestrator = CreateOrchestrator(extractor, fileWriter, new FakeTranslationService(),
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile
                });

            var success = await RunUpdateAsync(orchestrator, updateAll);

            Assert.True(success);
            Assert.Equal(new[] { "_maintainer" }.Concat(sourceKeys), ReadKeys(outputPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_RejectsEmptySource_WithoutChangingPack(bool updateAll)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromSourceJson(tempDir, "{}");
            var fakeService = new FakeTranslationService();
            var outputPath = Path.Combine(tempDir, "french.json");
            await File.WriteAllTextAsync(outputPath, MetadataPack);
            var originalBytes = await File.ReadAllBytesAsync(outputPath);

            var orchestrator = CreateOrchestrator(extractor, new FileWriter(NullLogger<FileWriter>.Instance), fakeService,
                UpdateSettings(tempDir, inputFile, maxPruneRatio: "1"));

            var success = await RunUpdateAsync(orchestrator, updateAll);

            Assert.False(success);
            Assert.Empty(fakeService.SeenRequests);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(outputPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_RejectsPruningAboveThreshold_WithoutChangingPack(bool updateAll)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromSourceJson(tempDir,
                "{\"hello\":\"Hello\",\"bye\":\"\",\"NewA\":\"\"}");
            var fakeService = new FakeTranslationService();
            var outputPath = Path.Combine(tempDir, "french.json");
            await File.WriteAllTextAsync(outputPath, """
                {
                  "_maintainer": "Reviewer|https://example.org/reviewer",
                  "_source": "https://example.org/source",
                  "bye": "au revoir",
                  "CompatibilityOnly": "Traduction d'une autre version",
                  "hello": "bonjour",
                  "RuntimeOnly": "Traduction de plugin"
                }
                """);
            var originalBytes = await File.ReadAllBytesAsync(outputPath);

            var orchestrator = CreateOrchestrator(extractor, new FileWriter(NullLogger<FileWriter>.Instance), fakeService,
                new Dictionary<string, string?>
                {
                    ["Translation:OutputDirectory"] = tempDir,
                    ["Translation:InputFile"] = inputFile
                });

            var success = await RunUpdateAsync(orchestrator, updateAll);

            Assert.False(success);
            Assert.Empty(fakeService.SeenRequests);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(outputPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_PartialTranslationFailure_KeepsExistingKeysAndMetadata_AndFails(bool updateAll)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromSourceJson(tempDir,
                "{\"hello\":\"Hello\",\"bye\":\"\",\"NewA\":\"\",\"NewB\":\"\"}");
            var fakeService = new FakeTranslationService(r => r.Key == "NewB"
                ? new TranslationResponse(r.Key, "", false, "provider error")
                : new TranslationResponse(r.Key, $"fr-{r.Key}", true));
            var fileWriter = new FileWriter(NullLogger<FileWriter>.Instance);
            var outputPath = Path.Combine(tempDir, "french.json");
            await File.WriteAllTextAsync(outputPath, """
                {
                  "_maintainer": "Reviewer|https://example.org/reviewer",
                  "bye": "au revoir",
                  "extra": "supplémentaire",
                  "hello": "bonjour"
                }
                """);

            var orchestrator = CreateOrchestrator(extractor, fileWriter, fakeService,
                UpdateSettings(tempDir, inputFile, maxPruneRatio: "1"));

            var success = await RunUpdateAsync(orchestrator, updateAll);

            Assert.False(success);
            Assert.Equal(new[] { "_maintainer", "NewA", "bye", "extra", "hello" }, ReadKeys(outputPath));
            var written = await fileWriter.LoadExistingBackendTranslationsAsync(outputPath);
            Assert.Equal("fr-NewA", written["NewA"]);
            Assert.Equal("supplémentaire", written["extra"]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_TranslatorThrows_LeavesPackUnchanged(bool updateAll)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var fakeService = new FakeTranslationService(_ => throw new HttpRequestException("provider down"));
            var outputPath = Path.Combine(tempDir, "french.json");
            await File.WriteAllTextAsync(outputPath, """
                {
                  "_maintainer": "Reviewer|https://example.org/reviewer",
                  "hello": "bonjour"
                }
                """);
            var originalBytes = await File.ReadAllBytesAsync(outputPath);

            var orchestrator = CreateOrchestrator(extractor, new FileWriter(NullLogger<FileWriter>.Instance), fakeService,
                UpdateSettings(tempDir, inputFile));

            var success = await RunUpdateAsync(orchestrator, updateAll);

            Assert.False(success);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(outputPath));
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
    public async Task UpdateAllLanguagesAsync_ReportsPerLanguageResults_ForMixedPacks()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (extractor, inputFile) = CreateExtractorFromKnownTranslationsFile(tempDir);
            var frenchPath = Path.Combine(tempDir, "french.json");
            var germanPath = Path.Combine(tempDir, "german.json");
            await File.WriteAllTextAsync(frenchPath, MetadataPack);
            await File.WriteAllTextAsync(germanPath, """
                {
                  "_maintainer": "Reviewer|https://example.org/reviewer",
                  "bye": "tschüss",
                  "hello": "hallo",
                  "RuntimeOnly": "Plugin-Übersetzung"
                }
                """);
            var germanBytes = await File.ReadAllBytesAsync(germanPath);

            var orchestrator = CreateOrchestrator(extractor, new FileWriter(NullLogger<FileWriter>.Instance),
                new FakeTranslationService(), UpdateSettings(tempDir, inputFile));

            var results = await orchestrator.UpdateAllLanguagesAsync();

            Assert.Equal(2, results.Count);
            Assert.True(results["fr"]);
            Assert.False(results["de"]);
            Assert.Equal(new[] { "_maintainer", "_source", "bye", "hello" }, ReadKeys(frenchPath));
            Assert.Equal(germanBytes, await File.ReadAllBytesAsync(germanPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static async Task<bool> RunUpdateAsync(TranslationOrchestrator orchestrator, bool updateAll)
    {
        if (!updateAll)
        {
            return await orchestrator.UpdateLanguageAsync("fr");
        }

        var results = await orchestrator.UpdateAllLanguagesAsync();
        Assert.True(results.ContainsKey("fr"), "update-all must report a result for french.json");
        return results["fr"];
    }

    private static Dictionary<string, string?> UpdateSettings(string outputDirectory, string inputFile, string? maxPruneRatio = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Translation:OutputDirectory"] = outputDirectory,
            ["Translation:InputFile"] = inputFile,
            ["Translation:BatchSize"] = "10",
            ["Translation:DelayBetweenRequests"] = "0"
        };
        if (maxPruneRatio != null)
        {
            settings["Translation:MaxPruneRatio"] = maxPruneRatio;
        }
        return settings;
    }

    private static List<string> ReadKeys(string path) =>
        JObject.Parse(File.ReadAllText(path)).Properties().Select(p => p.Name).ToList();

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

    private static (TranslationExtractor, string) CreateExtractorFromKnownTranslationsFile(string baseDirectory) =>
        CreateExtractorFromSourceJson(baseDirectory,
            "{\n" +
            "  \"hello\": \"Hello\",\n" +
            "  \"bye\": \"\"\n" +
            "}");

    private static (TranslationExtractor, string) CreateExtractorFromSourceJson(string baseDirectory, string json)
    {
        var filePath = Path.Combine(baseDirectory, "Translations.Default.cs");

        var content = "public class Seed\n" +
                      "{\n" +
                      "    public void Load()\n" +
                      "    {\n" +
                      "        var knownTranslations = \"\"\"\n" +
                      json + "\n" +
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
