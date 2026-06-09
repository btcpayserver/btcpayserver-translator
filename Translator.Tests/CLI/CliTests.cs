using Xunit;

namespace BTCPayTranslator.Tests.CLI;

public class CliTests
{
    [Fact]
    public async Task ListLanguages_ReturnsZero_AndPrintsKnownLanguage()
    {
        var result = await CliTestHost.RunAsync(["list-languages"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Supported Languages", result.CombinedOutput);
        Assert.Contains("fr-FR", result.CombinedOutput);
        Assert.Contains("bs-BA", result.CombinedOutput);
    }

    [Fact]
    public async Task Translate_WithUnsupportedLanguage_ReturnsNonZero()
    {
        var result = await CliTestHost.RunAsync(["translate", "--language", "xx" ]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unsupported language code", result.CombinedOutput);
    }

    [Fact]
    public async Task Update_WhenTranslationFileMissing_ReturnsNonZero()
    {
        var outputDirectory = CreateTempDirectory();
        var inputFile = CreateKnownTranslationsInputFile();
        try
        {
            var result = await CliTestHost.RunAsync(
                ["update", "--language", "fr"],
                new Dictionary<string, string?>
                {
                    ["Translation__OutputDirectory"] = outputDirectory,
                    ["Translation__InputFile"] = inputFile
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Translation file not found", result.CombinedOutput);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
            
            if (File.Exists(inputFile))
            {
                File.Delete(inputFile);
            }
        }
    }

    [Fact]
    public async Task UpdateAll_WhenNoTranslationFilesFound_ReturnsNonZero()
    {
        var outputDirectory = CreateTempDirectory();
        try
        {
            var result = await CliTestHost.RunAsync(
                ["update-all"],
                new Dictionary<string, string?>
                {
                    ["Translation__OutputDirectory"] = outputDirectory
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("No translation files found", result.CombinedOutput);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task BatchUpdate_WithContinueOnError_ProcessesMultipleLanguages_AndReturnsNonZero()
    {
        var outputDirectory = CreateTempDirectory();
        try
        {
            var result = await CliTestHost.RunAsync(
                ["batch-update", "--languages", "fr", "xx", "--continue-on-error"],
                new Dictionary<string, string?>
                {
                    ["Translation__OutputDirectory"] = outputDirectory
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Unsupported language code: xx", result.CombinedOutput);
            Assert.Matches(@"Batch update completed: \d+/2", result.CombinedOutput);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ValidatePacks_WithSuspiciousEntries_ReturnsNonZero()
    {
        var outputDirectory = CreateTempDirectory();
        var translationFile = Path.Combine(outputDirectory, "french.json");

        try
        {
            await File.WriteAllTextAsync(translationFile, """
                {
                  "hello": "bonjour",
                  "prompt": "please provide the english text"
                }
                """);

            var result = await CliTestHost.RunAsync(
                ["validate-packs"],
                new Dictionary<string, string?>
                {
                    ["Translation__OutputDirectory"] = outputDirectory
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Validation completed", result.CombinedOutput);
            Assert.Contains("Suspicious LLM/meta-response content", result.CombinedOutput);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RefreshKeys_InsertsMissingKeys_FromLocalSourceFile_AndReturnsZero()
    {
        var outputDirectory = CreateTempDirectory();
        var inputFile = CreateKnownTranslationsInputFile();
        var frenchFile = Path.Combine(outputDirectory, "french.json");

        try
        {
            await File.WriteAllTextAsync(frenchFile, "{\r\n  \"existing\": \"valeur\"\r\n}");

            var result = await CliTestHost.RunAsync(
                ["refresh-keys", "--source-file", inputFile],
                new Dictionary<string, string?>
                {
                    ["Translation__OutputDirectory"] = outputDirectory,
                    ["OPENROUTER_API_KEY"] = "" // refresh-keys must work without OpenRouter configured
                });

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Refresh completed", result.CombinedOutput);

            var written = await File.ReadAllTextAsync(frenchFile);
            Assert.Contains("\"hello\"", written);                 // new source key inserted
            Assert.Contains("\"existing\": \"valeur\"", written);  // existing entry untouched
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }

            if (File.Exists(inputFile))
            {
                File.Delete(inputFile);
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.CliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateKnownTranslationsInputFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.CliTests", Guid.NewGuid().ToString("N") + ".cs");
        var parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);

        var content = "public class Seed\n" +
                      "{\n" +
                      "    public void Load()\n" +
                      "    {\n" +
                      "        var knownTranslations = \"\"\"\n" +
                      "{\n" +
                      "  \"hello\": \"Hello\"\n" +
                      "}\n" +
                      "\"\"\";\n" +
                      "    }\n" +
                      "}\n";

        File.WriteAllText(path, content);
        return path;
    }
}
