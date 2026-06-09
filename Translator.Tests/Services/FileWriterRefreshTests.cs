using System.Text;
using BTCPayTranslator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayTranslator.Tests.Services;

public class FileWriterRefreshTests
{
    // Build a CRLF JSON document from individual lines (no trailing newline unless requested).
    private static string Crlf(params string[] lines) => string.Join("\r\n", lines);

    private static FileWriter Sut() => new(NullLogger<FileWriter>.Instance);

    private static Dictionary<string, string> Source(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value);

    [Fact]
    public async Task InsertMissingKeysAsync_InsertsNewKey_InCorrectSortedPosition()
    {
        var file = WriteTemp(Crlf(
            "{",
            "  \"a\": \"A\",",
            "  \"c\": \"C\"",
            "}"));
        try
        {
            var added = await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("b", "B"), ("c", "C")));

            Assert.Equal(1, added);
            var keys = JObject.Parse(await File.ReadAllTextAsync(file)).Properties().Select(p => p.Name).ToList();
            Assert.Equal(new[] { "a", "b", "c" }, keys);
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_PlaceholderValue_EqualsEnglishSource()
    {
        var file = WriteTemp(Crlf("{", "  \"a\": \"A\"", "}"));
        try
        {
            await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("b", "English B")));

            var json = JObject.Parse(await File.ReadAllTextAsync(file));
            Assert.Equal("English B", json["b"]!.Value<string>());
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_PreservesExistingLines_AndEmptyValues_AndNonAscii()
    {
        var existingLines = new[]
        {
            "  \"_maintainer\": \"someone|https://example.com\",",
            "  \"déjà\": \"déjà vu\",",
            "  \"empty\": \"\",",
            "  \"zed\": \"Z\""
        };
        var file = WriteTemp(Crlf(new[] { "{" }.Concat(existingLines).Append("}").ToArray()));
        try
        {
            var added = await Sut().InsertMissingKeysAsync(file, Source(("mango", "Mango"), ("zed", "Z")));

            Assert.Equal(1, added);
            var text = await File.ReadAllTextAsync(file);

            // Every original entry line survives verbatim.
            foreach (var line in existingLines)
                Assert.Contains(line, text);

            // Empty value preserved, non-ASCII left raw (no \u escapes anywhere).
            Assert.DoesNotContain("\\u", text);
            var json = JObject.Parse(text);
            Assert.Equal("", json["empty"]!.Value<string>());
            Assert.Equal("déjà vu", json["déjà"]!.Value<string>());
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_PreservesTrailingSpaceOnExistingLine()
    {
        // A non-last line that ends with ", " (comma + trailing space) must stay byte-identical.
        var spacey = "  \"a\": \"A\", ";
        var file = WriteTemp(Crlf("{", spacey, "  \"c\": \"C\"", "}"));
        try
        {
            await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("b", "B"), ("c", "C")));

            var lines = (await File.ReadAllTextAsync(file)).Split("\r\n");
            Assert.Contains(spacey, lines);
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_DoesNotReorderExisting_InNonCanonicalOrderFile()
    {
        // Keys deliberately NOT in writer order.
        var file = WriteTemp(Crlf(
            "{",
            "  \"_maintainer\": \"x|https://e.com\",",
            "  \"zebra\": \"Z\",",
            "  \"alpha\": \"A\"",
            "}"));
        try
        {
            await Sut().InsertMissingKeysAsync(file, Source(("zebra", "Z"), ("alpha", "A"), ("mango", "M")));

            var keys = JObject.Parse(await File.ReadAllTextAsync(file)).Properties().Select(p => p.Name).ToList();
            // Existing relative order is preserved; only positions of the 3 pre-existing keys matter here.
            Assert.True(keys.IndexOf("_maintainer") < keys.IndexOf("zebra"));
            Assert.True(keys.IndexOf("zebra") < keys.IndexOf("alpha"));
            Assert.Contains("mango", keys);
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_IsIdempotent()
    {
        var file = WriteTemp(Crlf("{", "  \"a\": \"A\",", "  \"c\": \"C\"", "}"));
        try
        {
            var first = await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("b", "B"), ("c", "C")));
            var afterFirst = await File.ReadAllBytesAsync(file);

            var second = await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("b", "B"), ("c", "C")));
            var afterSecond = await File.ReadAllBytesAsync(file);

            Assert.Equal(1, first);
            Assert.Equal(0, second);
            Assert.Equal(afterFirst, afterSecond);
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_PreservesTrailingNewline_WhenPresent()
    {
        var file = WriteTemp(Crlf("{", "  \"a\": \"A\"", "}") + "\r\n");
        try
        {
            await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("b", "B")));
            Assert.EndsWith("}\r\n", await File.ReadAllTextAsync(file));
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_PreservesNoTrailingNewline_WhenAbsent()
    {
        var file = WriteTemp(Crlf("{", "  \"a\": \"A\"", "}"));
        try
        {
            await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("b", "B")));
            var text = await File.ReadAllTextAsync(file);
            Assert.EndsWith("}", text);
            Assert.False(text.EndsWith("}\r\n"));
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_InsertingAfterLastKey_FixesPreviousLastComma()
    {
        var file = WriteTemp(Crlf("{", "  \"a\": \"A\"", "}"));
        try
        {
            await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("z", "Z")));

            var lines = (await File.ReadAllTextAsync(file)).Split("\r\n");
            Assert.Equal("  \"a\": \"A\",", lines[1]); // gained a comma
            Assert.Equal("  \"z\": \"Z\"", lines[2]);  // new last, no comma
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_InsertsAtTop_WhenNewKeyPrecedesAllExisting()
    {
        var file = WriteTemp(Crlf("{", "  \"m\": \"M\"", "}"));
        try
        {
            await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("m", "M")));

            var keys = JObject.Parse(await File.ReadAllTextAsync(file)).Properties().Select(p => p.Name).ToList();
            Assert.Equal(new[] { "a", "m" }, keys);
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task InsertMissingKeysAsync_ReturnsZero_OnMissingFile()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var added = await Sut().InsertMissingKeysAsync(missing, Source(("a", "A")));
        Assert.Equal(0, added);
    }

    [Fact]
    public async Task InsertMissingKeysAsync_RendersValueWithNewline_AsOnePhysicalLine()
    {
        var file = WriteTemp(Crlf("{", "  \"a\": \"A\"", "}"));
        try
        {
            // Source value contains an actual newline; it must be escaped as \n on a single line.
            await Sut().InsertMissingKeysAsync(file, Source(("a", "A"), ("multi", "line1\nline2")));

            var text = await File.ReadAllTextAsync(file);
            var lines = text.Split("\r\n");
            Assert.Equal(4, lines.Length); // { , "a" , "multi" , }
            Assert.Contains(lines, l => l.Contains("\"multi\"") && l.Contains("line1\\nline2"));
            Assert.Equal("line1\nline2", JObject.Parse(text)["multi"]!.Value<string>());
        }
        finally { Cleanup(file); }
    }

    private static string WriteTemp(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "french.json");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static void Cleanup(string file)
    {
        var dir = Path.GetDirectoryName(file)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}
