using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using BTCPayTranslator.Models;
using BTCPayTranslator.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace BTCPayTranslator.Tests.Services;

public class FileWriterLoadSafetyTests
{
    [Theory]
    [InlineData(LoadSafetyFixture.Malformed)]
    [InlineData("")]
    public async Task Load_PropagatesParseFailure_AndPreservesBytes(string content)
    {
        using var fixture = new LoadSafetyFixture();
        var original = fixture.WritePack(content);

        await Assert.ThrowsAsync<JsonReaderException>(() => fixture.Writer.LoadExistingBackendTranslationsAsync(fixture.PackPath));

        Assert.Equal(original, File.ReadAllBytes(fixture.PackPath));
    }

    [Fact]
    public async Task Load_PropagatesMissingFile_AfterItDisappears()
    {
        using var fixture = new LoadSafetyFixture();
        fixture.WritePack(LoadSafetyFixture.Populated);
        Assert.True(File.Exists(fixture.PackPath));
        File.Delete(fixture.PackPath);

        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Writer.LoadExistingBackendTranslationsAsync(fixture.PackPath));

        Assert.False(File.Exists(fixture.PackPath));
    }

    [Theory]
    [InlineData("{}", 0)]
    [InlineData(LoadSafetyFixture.Populated, 1)]
    public async Task Load_AcceptsValidEmptyAndPopulatedPacks(string content, int count)
    {
        using var fixture = new LoadSafetyFixture();
        var original = fixture.WritePack(content);

        var loaded = await fixture.Writer.LoadExistingBackendTranslationsAsync(fixture.PackPath);

        Assert.Equal(count, loaded.Count);
        if (count > 0)
            Assert.Equal("Human", loaded["Pay"]);
        Assert.Equal(original, File.ReadAllBytes(fixture.PackPath));
    }
}

public class TranslationOrchestratorLoadSafetyTests
{
    public static IEnumerable<object[]> ParseFailures =>
        from all in new[] { false, true }
        from content in new[] { LoadSafetyFixture.Malformed, "", "{\"_maintainer\":\"Reviewer|https://example.org/reviewer\",\"Pay\":\"Human\"" }
        from response in new[] { "success", "failure", "throw" }
        select new object[] { all, content, response };

    [Theory]
    [MemberData(nameof(ParseFailures))]
    public async Task Update_AbortsBeforeTranslation_OnParseFailure(bool all, string content, string response)
    {
        using var fixture = new LoadSafetyFixture(response);
        var original = fixture.WritePack(content);

        Assert.False(await fixture.UpdateAsync(all));

        fixture.AssertNoTranslationOrWrite<JsonReaderException>();
        Assert.Equal(original, File.ReadAllBytes(fixture.PackPath));
    }

    [Theory]
    [InlineData(false, "success")]
    [InlineData(false, "failure")]
    [InlineData(false, "throw")]
    [InlineData(true, "success")]
    [InlineData(true, "failure")]
    [InlineData(true, "throw")]
    public async Task Update_AbortsBeforeTranslation_OnExclusiveLock(bool all, string response)
    {
        using var fixture = new LoadSafetyFixture(response);
        var original = fixture.WritePack(LoadSafetyFixture.Populated);
        using (var exclusive = new FileStream(fixture.PackPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => File.ReadAllTextAsync(fixture.PackPath));
            await Assert.ThrowsAnyAsync<IOException>(() => fixture.Writer.LoadExistingBackendTranslationsAsync(fixture.PackPath));

            Assert.False(await fixture.UpdateAsync(all));

            fixture.AssertNoTranslationOrWrite<IOException>();
        }
        Assert.Equal(original, File.ReadAllBytes(fixture.PackPath));
    }

    [WindowsTheory]
    [InlineData(false, "success")]
    [InlineData(false, "failure")]
    [InlineData(false, "throw")]
    [InlineData(true, "success")]
    [InlineData(true, "failure")]
    [InlineData(true, "throw")]
    public async Task Update_AbortsBeforeTranslation_OnReadDenied(bool all, string response)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var fixture = new LoadSafetyFixture(response);
        var original = fixture.WritePack(LoadSafetyFixture.Populated);
        var file = new FileInfo(fixture.PackPath);
        var originalAccess = file.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        var deniedAccess = new FileSecurity();
        deniedAccess.SetSecurityDescriptorBinaryForm(originalAccess, AccessControlSections.Access);
        using var identity = WindowsIdentity.GetCurrent();
        deniedAccess.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            file.SetAccessControl(deniedAccess);
            Assert.True(File.Exists(fixture.PackPath));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => File.ReadAllTextAsync(fixture.PackPath));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Writer.LoadExistingBackendTranslationsAsync(fixture.PackPath));

            Assert.False(await fixture.UpdateAsync(all));

            fixture.AssertNoTranslationOrWrite<UnauthorizedAccessException>();
        }
        finally
        {
            var restoredAccess = new FileSecurity();
            restoredAccess.SetSecurityDescriptorBinaryForm(originalAccess, AccessControlSections.Access);
            file.SetAccessControl(restoredAccess);
        }
        Assert.Equal(original, File.ReadAllBytes(fixture.PackPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_ValidPopulatedPack_IsANoOp(bool all)
    {
        using var fixture = new LoadSafetyFixture();
        var original = fixture.WritePack(LoadSafetyFixture.Populated);

        Assert.True(await fixture.UpdateAsync(all));

        Assert.Empty(fixture.Service.SeenRequests);
        Assert.Equal(original, File.ReadAllBytes(fixture.PackPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_ValidEmptyPack_CanBePopulated(bool all)
    {
        using var fixture = new LoadSafetyFixture();
        fixture.WritePack("{}");

        Assert.True(await fixture.UpdateAsync(all));

        Assert.Equal("Pay", Assert.Single(fixture.Service.SeenRequests).Key);
        var loaded = await fixture.Writer.LoadExistingBackendTranslationsAsync(fixture.PackPath);
        Assert.Equal("translated-Pay", loaded["Pay"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_ValidPack_PreservesExistingTranslationsAndObsoleteKeyRemoval(bool all)
    {
        using var fixture = new LoadSafetyFixture(source: "{\"Pay\":\"\",\"Refund\":\"\"}");
        fixture.WritePack("{\"Pay\":\"Human\",\"obsolete\":\"Old\"}");

        Assert.True(await fixture.UpdateAsync(all));

        Assert.Equal("Refund", Assert.Single(fixture.Service.SeenRequests).Key);
        var loaded = await fixture.Writer.LoadExistingBackendTranslationsAsync(fixture.PackPath);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("Human", loaded["Pay"]);
        Assert.Equal("translated-Refund", loaded["Refund"]);
        Assert.False(loaded.ContainsKey("obsolete"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Translate_CanCreateAnInitiallyMissingPack(bool force)
    {
        using var fixture = new LoadSafetyFixture();

        Assert.True(await fixture.Orchestrator.TranslateToLanguageAsync("sw", force));

        Assert.Equal("Pay", Assert.Single(fixture.Service.SeenRequests).Key);
        var loaded = await fixture.Writer.LoadExistingBackendTranslationsAsync(fixture.PackPath);
        Assert.Equal("translated-Pay", loaded["Pay"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Translate_AbortsBeforeTranslation_OnExistingMalformedPack_EvenWhenForced(bool force)
    {
        using var fixture = new LoadSafetyFixture();
        var original = fixture.WritePack(LoadSafetyFixture.Malformed);

        Assert.False(await fixture.Orchestrator.TranslateToLanguageAsync("sw", force));

        fixture.AssertNoTranslationOrWrite<JsonReaderException>();
        Assert.Equal(original, File.ReadAllBytes(fixture.PackPath));
    }

    [Fact]
    public async Task Update_MissingPack_IsNotCreated()
    {
        using var fixture = new LoadSafetyFixture();

        Assert.False(await fixture.Orchestrator.UpdateLanguageAsync("sw"));
        Assert.Empty(await fixture.Orchestrator.UpdateAllLanguagesAsync());

        Assert.Empty(fixture.Service.SeenRequests);
        Assert.False(File.Exists(fixture.PackPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAll_DisappearingDiscoveredPack_IsRecordedAsFailure(bool continueOnError)
    {
        using var fixture = new LoadSafetyFixture();
        fixture.WritePack(LoadSafetyFixture.Populated);
        fixture.OnSourceFetch = () => File.Delete(fixture.PackPath);

        var results = await fixture.Orchestrator.UpdateAllLanguagesAsync(continueOnError);

        Assert.False(Assert.Single(results).Value);
        Assert.Empty(fixture.Service.SeenRequests);
        Assert.False(File.Exists(fixture.PackPath));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Update_MixedPacks_RecordsFailureAndRespectsContinueOnError(bool all, bool continueOnError)
    {
        using var fixture = new LoadSafetyFixture();
        var broken = fixture.WritePack(LoadSafetyFixture.Malformed);
        var healthyPath = Path.Combine(fixture.OutputDirectory, "french.json");
        var healthy = Encoding.UTF8.GetBytes(LoadSafetyFixture.Populated);
        File.WriteAllBytes(healthyPath, healthy);

        var results = all
            ? await fixture.Orchestrator.UpdateAllLanguagesAsync(continueOnError)
            : await fixture.Orchestrator.UpdateMultipleLanguagesAsync(new[] { "sw", "fr" }, continueOnError);

        Assert.False(results["sw"]);
        if (continueOnError)
        {
            Assert.Equal(2, results.Count);
            Assert.True(results["fr"]);
        }
        else
        {
            // update-all follows filesystem discovery order; a healthy pack may precede the failure.
            Assert.Equal("sw", results.Last().Key);
            if (!all)
                Assert.Single(results);
        }
        fixture.AssertNoTranslationOrWrite<JsonReaderException>();
        Assert.Equal(broken, File.ReadAllBytes(fixture.PackPath));
        Assert.Equal(healthy, File.ReadAllBytes(healthyPath));
    }
}

internal sealed class LoadSafetyFixture : IDisposable
{
    public const string Malformed = "{\"Pay\":\"Human\"";
    public const string Populated = "{\r\n  \"Pay\": \"Human\"\r\n}\r\n";
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "BTCPayTranslator.LoadSafetyTests", Guid.NewGuid().ToString("N"));
    public string OutputDirectory => Path.Combine(DirectoryPath, "packs");
    public string PackPath => Path.Combine(OutputDirectory, "swahili.json");
    public string SourceFile => Path.Combine(DirectoryPath, "source.cs");
    public Action? OnSourceFetch { get; set; }
    public FakeTranslationService Service { get; }
    public RecordingLogger<FileWriter> WriterLog { get; } = new();
    public RecordingLogger<TranslationOrchestrator> OperationLog { get; } = new();
    public FileWriter Writer { get; }
    public TranslationOrchestrator Orchestrator { get; }
    private readonly HttpClient _httpClient;

    public LoadSafetyFixture(string response = "success", string source = "{\"Pay\":\"\"}")
    {
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(SourceFile, "var knownTranslations = \"\"\"\n" + source + "\n\"\"\";");
        Service = new FakeTranslationService(request => response switch
        {
            "failure" => new TranslationResponse(request.Key, "", false, "Injected failure"),
            "throw" => throw new InvalidOperationException("Injected translator exception"),
            _ => new TranslationResponse(request.Key, "translated-" + request.Key, true)
        });
        _httpClient = new HttpClient(new QueueHttpMessageHandler(_ =>
        {
            OnSourceFetch?.Invoke();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(source) };
        }));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Translation:OutputDirectory"] = OutputDirectory,
            ["Translation:BTCPayUrl"] = "https://btcpay.test",
            ["Translation:BatchSize"] = "10",
            ["Translation:DelayBetweenRequests"] = "0"
        }).Build();
        Writer = new FileWriter(WriterLog);
        Orchestrator = new TranslationOrchestrator(Service,
            new TranslationExtractor(NullLogger<TranslationExtractor>.Instance, _httpClient), Writer, configuration, OperationLog);
    }

    public byte[] WritePack(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(PackPath, bytes);
        return bytes;
    }

    public async Task<bool> UpdateAsync(bool all)
    {
        if (!all)
            return await Orchestrator.UpdateLanguageAsync("sw");
        var results = await Orchestrator.UpdateAllLanguagesAsync();
        Assert.Single(results);
        return results["sw"];
    }

    public void AssertNoTranslationOrWrite<TException>() where TException : Exception
    {
        Assert.Empty(Service.SeenRequests);
        // The real writer logs every successful write and every failed write attempt.
        Assert.DoesNotContain(WriterLog.Messages, message => message.Contains("backend translation", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(WriterLog.Errors);
        Assert.IsAssignableFrom<TException>(Assert.Single(OperationLog.Errors));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        Directory.Delete(DirectoryPath, recursive: true);
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();
    public List<Exception> Errors { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        if (exception != null)
            Errors.Add(exception);
    }
}

public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "ReadData denial uses Windows ACLs on the test-owned temporary file.";
    }
}
