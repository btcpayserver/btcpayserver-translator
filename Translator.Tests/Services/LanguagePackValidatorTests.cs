using BTCPayTranslator.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayTranslator.Tests.Services;

public class LanguagePackValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsIssue_WhenOutputDirectoryDoesNotExist()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.Tests", Guid.NewGuid().ToString("N"));
        var sut = CreateSut(missingDirectory);

        var result = await sut.ValidateAsync(fix: false);

        Assert.Equal(0, result.FilesScanned);
        Assert.Equal(0, result.EntriesScanned);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("<none>", issue.FileName);
        Assert.Contains("does not exist", issue.Reason);
    }

    [Fact]
    public async Task ValidateAsync_ReportsInvalidJsonFiles()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "broken.json"), "{\"hello\":");
            var sut = CreateSut(tempDir);

            var result = await sut.ValidateAsync(fix: false);

            Assert.Equal(1, result.FilesScanned);
            Assert.Equal(0, result.EntriesScanned);
            var issue = Assert.Single(result.Issues);
            Assert.Equal("broken.json", issue.FileName);
            Assert.Equal("<file>", issue.Key);
            Assert.Contains("Invalid JSON", issue.Reason);
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
    public async Task ValidateAsync_WithFix_RewritesMetaAndPlaceholderIssues()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(tempDir, "french.json");
            await File.WriteAllTextAsync(filePath, """
                {
                  "code": "fr",
                  "currentLanguage": "French",
                  "hello {name}": "bonjour",
                  "prompt": "please provide the english text"
                }
                """);

            var sut = CreateSut(tempDir);
            var result = await sut.ValidateAsync(fix: true);

            Assert.Equal(1, result.FilesScanned);
            Assert.Equal(4, result.EntriesScanned);
            Assert.Equal(2, result.Issues.Count);
            Assert.Contains(result.Issues, i => i.Key == "hello {name}" && i.Reason.Contains("Placeholder/token mismatch"));
            Assert.Contains(result.Issues, i => i.Key == "prompt" && i.Reason.Contains("Suspicious LLM/meta-response"));

            var written = JObject.Parse(await File.ReadAllTextAsync(filePath));
            Assert.Equal("hello {name}", written["hello {name}"]?.Value<string>());
            Assert.Equal("prompt", written["prompt"]?.Value<string>());
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
    public async Task ValidateAsync_WithFix_RemovesShortHotspotAndSentenceFallback()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var longKey = "This is a long sentence that should be translated";
            var filePath = Path.Combine(tempDir, "french.json");
            await File.WriteAllTextAsync(filePath, $$"""
                {
                  "Confirm": "Confirm",
                  "{{longKey}}": "{{longKey}}",
                  "hello": "bonjour"
                }
                """);

            var sut = CreateSut(tempDir);
            var result = await sut.ValidateAsync(fix: true);

            Assert.Equal(2, result.Issues.Count);
            Assert.Contains(result.Issues, i => i.Key == "Confirm" && i.Reason.Contains("left untranslated"));
            Assert.Contains(result.Issues, i => i.Key == longKey && i.Reason.Contains("sentence-like"));

            var written = JObject.Parse(await File.ReadAllTextAsync(filePath));
            Assert.Null(written["Confirm"]);
            Assert.Null(written[longKey]);
            Assert.Equal("bonjour", written["hello"]?.Value<string>());
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
    public async Task ValidateAsync_FlagsHtmlTagMismatch()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(tempDir, "hindi.json");
            await File.WriteAllTextAsync(filePath, """
                {
                  "<strong>Never</strong> trust anything but <code>id</code>": "केवल <code>id</code> पर भरोसा करें",
                  "kept-intact": "<code>foo</code> bar <code>baz</code>"
                }
                """.Replace("<code>foo</code> bar <code>baz</code>",
                            "<code>foo</code> bar <code>baz</code>"));

            // Re-write with a balanced kept-intact entry so only the first entry fails the rule
            await File.WriteAllTextAsync(filePath, """
                {
                  "<strong>Never</strong> trust anything but <code>id</code>": "केवल <code>id</code> पर भरोसा करें",
                  "<code>foo</code>": "<code>foo</code>"
                }
                """);

            var sut = CreateSut(tempDir);
            var result = await sut.ValidateAsync(fix: false);

            Assert.Equal(2, result.EntriesScanned);
            var issue = Assert.Single(result.Issues);
            Assert.StartsWith("<strong>Never", issue.Key);
            Assert.Contains("Structural HTML tag mismatch", issue.Reason);
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
    public async Task ValidateAsync_IgnoresExampleEmailAngleBrackets()
    {
        // The HTML-tag check uses a curated allowlist of structural elements
        // (strong/em/code/br/p/a/etc.) so localized example data like
        // "<email@primer.com>" doesn't trip the rule even though the bare
        // HtmlTagRegex would match it.
        var tempDir = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(tempDir, "serbian.json");
            await File.WriteAllTextAsync(filePath, """
                {
                  "Firstname Lastname <email@example.com>": "Ime Prezime <email@primer.com>"
                }
                """);

            var sut = CreateSut(tempDir);
            var result = await sut.ValidateAsync(fix: false);

            Assert.Equal(1, result.EntriesScanned);
            Assert.Empty(result.Issues);
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
    public async Task ValidateAsync_FlagsInvalidMaintainerField()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(tempDir, "bad-maintainer.json");
            await File.WriteAllTextAsync(filePath, """
                {
                  "_maintainer": "someone with no pipe or URL",
                  "hello": "bonjour"
                }
                """);

            var sut = CreateSut(tempDir);
            var result = await sut.ValidateAsync(fix: false);

            // _maintainer is not counted as a translation entry
            Assert.Equal(1, result.EntriesScanned);
            var issue = Assert.Single(result.Issues);
            Assert.Equal("_maintainer", issue.Key);
            Assert.Contains("Invalid _maintainer value", issue.Reason);
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
    public async Task ValidateAsync_AcceptsWellFormedMaintainerField()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(tempDir, "ok-maintainer.json");
            await File.WriteAllTextAsync(filePath, """
                {
                  "_maintainer": "thgO-O|https://github.com/thgO-O",
                  "hello": "olá"
                }
                """);

            var sut = CreateSut(tempDir);
            var result = await sut.ValidateAsync(fix: false);

            Assert.Equal(1, result.EntriesScanned);
            Assert.Empty(result.Issues);
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
    public async Task ValidateAsync_RejectsMaintainerWithHttpScheme()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(tempDir, "http-maintainer.json");
            await File.WriteAllTextAsync(filePath, """
                {
                  "_maintainer": "thgO-O|http://github.com/thgO-O"
                }
                """);

            var sut = CreateSut(tempDir);
            var result = await sut.ValidateAsync(fix: false);

            var issue = Assert.Single(result.Issues);
            Assert.Equal("_maintainer", issue.Key);
            Assert.Contains("Invalid _maintainer", issue.Reason);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static LanguagePackValidator CreateSut(string outputDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Translation:OutputDirectory"] = outputDirectory
            })
            .Build();

        return new LanguagePackValidator(configuration, NullLogger<LanguagePackValidator>.Instance);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
