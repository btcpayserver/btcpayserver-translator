using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using System.CommandLine;
using BTCPayTranslator.Models;
using BTCPayTranslator.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DotNetEnv;

namespace BTCPayTranslator;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Load .env file if it exists
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        // Setup dependency injection
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, configuration);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        // Create command line interface
        var rootCommand = new RootCommand("BTCPay Server Translation Tool - Translate BTCPay Server to multiple languages using AI")
        {
            CreateTranslateCommand(serviceProvider),
            CreateListLanguagesCommand(),
            CreateBatchCommand(serviceProvider),
            CreateStatusCommand(serviceProvider),
            CreateUpdateCommand(serviceProvider),
            CreateBatchUpdateCommand(serviceProvider),
            CreateUpdateAllCommand(serviceProvider),
            CreateRefreshKeysCommand(serviceProvider),
            CreateValidatePacksCommand(serviceProvider),
            CreateGenerateManifestCommand(serviceProvider)
        };

        return await rootCommand.InvokeAsync(args);
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddConfiguration(configuration.GetSection("Logging"));
        });

        services.AddHttpClient();
        services.AddTransient<TranslationExtractor>();
        services.AddTransient<FileWriter>();
        services.AddTransient<TranslationOrchestrator>();
        services.AddTransient<LanguagePackValidator>();

        services.AddTransient<ITranslationService, BaseTranslationService>();

        services.AddTransient<ManifestGenerator>();
    }

    private static Option<string?> CreateBTCPayUrlOption() =>
        new Option<string?>(
            "--btcpay-url",
            "Base URL of a BTCPay Server running in debug/cheat mode " +
            "(e.g. http://localhost:14142). When set, translations are fetched " +
            "from the /cheat/translations/default-en endpoint instead of GitHub.")
        {
            IsRequired = false
        };

    private static void ApplyBTCPayUrl(IServiceProvider sp, string? btcpayUrl)
    {
        if (!string.IsNullOrWhiteSpace(btcpayUrl))
            sp.GetRequiredService<IConfiguration>()["Translation:BTCPayUrl"] = btcpayUrl;
    }

    private static void ApplyInputFile(IServiceProvider sp, string? sourceFile)
    {
        if (!string.IsNullOrWhiteSpace(sourceFile))
            sp.GetRequiredService<IConfiguration>()["Translation:InputFile"] = sourceFile;
    }

    private static Command CreateTranslateCommand(ServiceProvider serviceProvider)
    {
        var languageOption = new Option<string>(
            "--language",
            "Language code to translate to (e.g., 'hi', 'es', 'fr')")
        {
            IsRequired = true
        };

        var forceOption = new Option<bool>(
            "--force",
            "Force retranslation of all strings, even if translations already exist");

        var btcpayUrlOption = CreateBTCPayUrlOption();

        var command = new Command("translate", "Translate BTCPay Server to a specific language")
        {
            languageOption,
            forceOption,
            btcpayUrlOption
        };

        command.SetHandler(async (language, force, btcpayUrl) =>
        {
            using var scope = serviceProvider.CreateScope();
            ApplyBTCPayUrl(scope.ServiceProvider, btcpayUrl);
            var orchestrator = scope.ServiceProvider.GetRequiredService<TranslationOrchestrator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting translation for language: {Language}", language);
            
            var success = await orchestrator.TranslateToLanguageAsync(language, force);
            
            if (success)
            {
                logger.LogInformation("Translation completed successfully!");
            }
            else
            {
                logger.LogError("Translation failed!");
                Environment.Exit(1);
            }
        }, languageOption, forceOption, btcpayUrlOption);

        return command;
    }

    private static Command CreateBatchCommand(ServiceProvider serviceProvider)
    {
        var languagesOption = new Option<string[]>(
            "--languages",
            "Multiple language codes to translate to (e.g., 'hi es fr')")
        {
            IsRequired = true,
            AllowMultipleArgumentsPerToken = true
        };

        var forceOption = new Option<bool>(
            "--force",
            "Force retranslation of all strings, even if translations already exist");

        var continueOnErrorOption = new Option<bool>(
            "--continue-on-error",
            "Continue processing other languages if one fails")
        {
            IsRequired = false
        };

        var btcpayUrlOption = CreateBTCPayUrlOption();

        var command = new Command("batch", "Translate BTCPay Server to multiple languages")
        {
            languagesOption,
            forceOption,
            continueOnErrorOption,
            btcpayUrlOption
        };

        command.SetHandler(async (languages, force, continueOnError, btcpayUrl) =>
        {
            using var scope = serviceProvider.CreateScope();
            ApplyBTCPayUrl(scope.ServiceProvider, btcpayUrl);
            var orchestrator = scope.ServiceProvider.GetRequiredService<TranslationOrchestrator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting batch translation for languages: {Languages}", 
                string.Join(", ", languages));
            
            var results = await orchestrator.TranslateToMultipleLanguagesAsync(languages, force, continueOnError);
            
            var successCount = results.Values.Count(success => success);
            var totalCount = results.Count;
            
            logger.LogInformation("Batch translation completed: {SuccessCount}/{TotalCount} successful", 
                successCount, totalCount);
                
            foreach (var result in results)
            {
                var status = result.Value ? "✓" : "✗";
                logger.LogInformation("  {Status} {Language}", status, result.Key);
            }
            
            if (successCount < totalCount)
            {
                Environment.Exit(1);
            }
        }, languagesOption, forceOption, continueOnErrorOption, btcpayUrlOption);

        return command;
    }

    private static Command CreateListLanguagesCommand()
    {
        var command = new Command("list-languages", "List all supported languages");

        command.SetHandler(() =>
        {
            Console.WriteLine("Supported Languages:");
            Console.WriteLine("===================");
            
            foreach (var lang in SupportedLanguages.GetAllLanguages().OrderBy(l => l.Name))
            {
                Console.WriteLine($"{lang.Code,-10} {lang.Name,-20} {lang.NativeName}");
            }
        });

        return command;
    }

    private static Command CreateStatusCommand(ServiceProvider serviceProvider)
    {
        var command = new Command("status", "Show translation status for all languages");

        command.SetHandler(async () =>
        {
            using var scope = serviceProvider.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var fileWriter = scope.ServiceProvider.GetRequiredService<FileWriter>();
            
            var outputDir = configuration["Translation:OutputDirectory"] ?? 
                           "translations";

            Console.WriteLine("Translation Status:");
            Console.WriteLine("==================");
            Console.WriteLine($"{"Language",-15} {"Code",-10} {"File Exists",-12} {"Translations",-12}");
            Console.WriteLine(new string('-', 55));

            foreach (var lang in SupportedLanguages.GetAllLanguages().OrderBy(l => l.Name))
            {
                var filePath = Path.Combine(outputDir, $"{lang.Name.ToLower()}.json");
                var exists = File.Exists(filePath);
                var count = 0;

                if (exists)
                {
                    try
                    {
                        var translations = await fileWriter.LoadExistingBackendTranslationsAsync(filePath);
                        count = translations.Count;
                    }
                    catch
                    {
                        // Ignore errors for status check
                    }
                }

                var existsText = exists ? "✓" : "✗";
                Console.WriteLine($"{lang.Name,-15} {lang.Code,-10} {existsText,-12} {count,-12}");
            }
        });

        return command;
    }

    private static Command CreateUpdateCommand(ServiceProvider serviceProvider)
    {
        var languageOption = new Option<string>(
            "--language",
            "Language code to update (e.g., 'hi', 'es', 'fr')")
        {
            IsRequired = true
        };

        var btcpayUrlOption = CreateBTCPayUrlOption();

        var command = new Command("update", "Update an existing translation file with new strings")
        {
            languageOption,
            btcpayUrlOption
        };

        command.SetHandler(async (language, btcpayUrl) =>
        {
            using var scope = serviceProvider.CreateScope();
            ApplyBTCPayUrl(scope.ServiceProvider, btcpayUrl);
            var orchestrator = scope.ServiceProvider.GetRequiredService<TranslationOrchestrator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting update for language: {Language}", language);
            
            var success = await orchestrator.UpdateLanguageAsync(language);
            
            if (success)
            {
                logger.LogInformation("Update completed successfully!");
            }
            else
            {
                logger.LogError("Update failed!");
                Environment.Exit(1);
            }
        }, languageOption, btcpayUrlOption);

        return command;
    }

    private static Command CreateBatchUpdateCommand(ServiceProvider serviceProvider)
    {
        var languagesOption = new Option<string[]>(
            "--languages",
            "Multiple language codes to update (e.g., 'hi es fr')")
        {
            IsRequired = true,
            AllowMultipleArgumentsPerToken = true
        };

        var continueOnErrorOption = new Option<bool>(
            "--continue-on-error",
            "Continue processing other languages if one fails")
        {
            IsRequired = false
        };

        var btcpayUrlOption = CreateBTCPayUrlOption();

        var command = new Command("batch-update", "Update multiple existing translation files with new strings")
        {
            languagesOption,
            continueOnErrorOption,
            btcpayUrlOption
        };

        command.SetHandler(async (languages, continueOnError, btcpayUrl) =>
        {
            using var scope = serviceProvider.CreateScope();
            ApplyBTCPayUrl(scope.ServiceProvider, btcpayUrl);
            var orchestrator = scope.ServiceProvider.GetRequiredService<TranslationOrchestrator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting batch update for languages: {Languages}", 
                string.Join(", ", languages));
            
            var results = await orchestrator.UpdateMultipleLanguagesAsync(languages, continueOnError);
            
            var successCount = results.Values.Count(success => success);
            var totalCount = results.Count;
            
            logger.LogInformation("Batch update completed: {SuccessCount}/{TotalCount} successful", 
                successCount, totalCount);
                
            foreach (var result in results)
            {
                var status = result.Value ? "✓" : "✗";
                logger.LogInformation("  {Status} {Language}", status, result.Key);
            }
            
            if (successCount < totalCount)
            {
                Environment.Exit(1);
            }
        }, languagesOption, continueOnErrorOption, btcpayUrlOption);

        return command;
    }

    private static Command CreateUpdateAllCommand(ServiceProvider serviceProvider)
    {
        var continueOnErrorOption = new Option<bool>(
            "--continue-on-error",
            "Continue processing other languages if one fails")
        {
            IsRequired = false
        };

        var btcpayUrlOption = CreateBTCPayUrlOption();

        var command = new Command("update-all", "Detect and update all existing translation files with new strings")
        {
            continueOnErrorOption,
            btcpayUrlOption
        };

        command.SetHandler(async (continueOnError, btcpayUrl) =>
        {
            using var scope = serviceProvider.CreateScope();
            ApplyBTCPayUrl(scope.ServiceProvider, btcpayUrl);
            var orchestrator = scope.ServiceProvider.GetRequiredService<TranslationOrchestrator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting update-all: detecting existing translation files...");
            
            var results = await orchestrator.UpdateAllLanguagesAsync(continueOnError);
            
            if (results.Count == 0)
            {
                logger.LogError("No translation files found to update");
                Environment.Exit(1);
                return;
            }
            
            var successCount = results.Values.Count(success => success);
            var totalCount = results.Count;
            
            logger.LogInformation("Update-all completed: {SuccessCount}/{TotalCount} successful", 
                successCount, totalCount);
                
            foreach (var result in results)
            {
                var status = result.Value ? "✓" : "✗";
                logger.LogInformation("  {Status} {Language}", status, result.Key);
            }
            
            if (successCount < totalCount)
            {
                Environment.Exit(1);
            }
        }, continueOnErrorOption, btcpayUrlOption);

        return command;
    }

    private static Command CreateRefreshKeysCommand(ServiceProvider serviceProvider)
    {
        var sourceFileOption = new Option<string?>(
            "--source-file",
            "Path to a local BTCPay Translations.Default.cs (or its JSON). When set, source keys are read " +
            "from this file instead of downloading from GitHub. Overrides the configured InputFile.")
        {
            IsRequired = false
        };

        var languagesOption = new Option<string[]>(
            "--languages",
            "Optional language codes to limit the refresh to (e.g. 'fr es'). Omit to refresh all files.")
        {
            IsRequired = false,
            AllowMultipleArgumentsPerToken = true
        };

        var btcpayUrlOption = CreateBTCPayUrlOption();

        var command = new Command(
            "refresh-keys",
            "Insert newly-added English source keys into existing translation files as English placeholders " +
            "(insert-only, no AI/OpenRouter, preserves existing lines).")
        {
            sourceFileOption,
            btcpayUrlOption,
            languagesOption
        };

        command.SetHandler(async (sourceFile, btcpayUrl, languages) =>
        {
            using var scope = serviceProvider.CreateScope();
            ApplyBTCPayUrl(scope.ServiceProvider, btcpayUrl);
            ApplyInputFile(scope.ServiceProvider, sourceFile);

            var orchestrator = scope.ServiceProvider.GetRequiredService<TranslationOrchestrator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            if (!string.IsNullOrWhiteSpace(btcpayUrl) && !string.IsNullOrWhiteSpace(sourceFile))
                logger.LogWarning("Both --btcpay-url and --source-file were provided; --btcpay-url takes precedence.");

            var codes = languages is { Length: > 0 } ? languages : null;
            var result = await orchestrator.RefreshKeysAsync(codes);

            logger.LogInformation(
                "Refresh completed: {TotalKeysAdded} key(s) added across {FilesProcessed} file(s) ({FilesSkipped} skipped).",
                result.TotalKeysAdded, result.FilesProcessed, result.FilesSkipped);

            if (result.FilesProcessed == 0)
            {
                logger.LogError("No translation files found to refresh.");
                Environment.Exit(1);
            }
        }, sourceFileOption, btcpayUrlOption, languagesOption);

        return command;
    }

    private static Command CreateValidatePacksCommand(ServiceProvider serviceProvider)
    {
        var fixOption = new Option<bool>(
            "--fix",
            "Automatically fixes suspicious entries by restoring English fallback text or removing hotspot keys.")
        {
            IsRequired = false
        };

        var command = new Command(
            "validate-packs",
            "Validate translation JSON files for suspicious LLM/meta responses and placeholder mismatches")
        {
            fixOption
        };

        command.SetHandler(async (fix) =>
        {
            using var scope = serviceProvider.CreateScope();
            var validator = scope.ServiceProvider.GetRequiredService<LanguagePackValidator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Validating translation packs (fix mode: {FixMode})", fix);
            var result = await validator.ValidateAsync(fix);

            if (fix)
            {
                // Fix passes are not strictly idempotent: a fix that removes one contamination
                // can surface an adjacent contamination that was previously masked. Loop until
                // a no-op pass (or an upper bound, to avoid pathological cycles).
                const int maxFixPasses = 10;
                var pass = 1;
                while (result.Issues.Count > 0 && pass < maxFixPasses)
                {
                    pass++;
                    logger.LogInformation(
                        "Re-running with fix=true (pass {Pass} of up to {MaxPasses}) - {IssueCount} issues remain",
                        pass, maxFixPasses, result.Issues.Count);
                    result = await validator.ValidateAsync(true);
                }

                logger.LogInformation("Re-running validation after fixes");
                result = await validator.ValidateAsync(false);

                if (pass == maxFixPasses && result.Issues.Count > 0)
                {
                    logger.LogWarning(
                        "--fix did not converge after {MaxPasses} passes. {RemainingCount} issue(s) remain and likely require manual review.",
                        maxFixPasses, result.Issues.Count);
                }
            }

            logger.LogInformation(
                "Validation completed: {FilesScanned} files, {EntriesScanned} entries, {IssueCount} issues",
                result.FilesScanned,
                result.EntriesScanned,
                result.Issues.Count);

            if (result.Issues.Count > 0)
            {
                foreach (var issue in result.Issues.Take(200))
                {
                    logger.LogError("{File}: '{Key}' -> {Reason}", issue.FileName, issue.Key, issue.Reason);
                }

                if (result.Issues.Count > 200)
                {
                    logger.LogError("... {RemainingCount} more issue(s) omitted from log", result.Issues.Count - 200);
                }

                Environment.Exit(1);
            }
        }, fixOption);

        return command;
    }
    
    private static Command CreateGenerateManifestCommand(ServiceProvider serviceProvider)
    {
        var projectDirectory = ResolveProjectDirectory();
        var defaultTranslationPath = Path.Combine(projectDirectory, "..", "translations");
        var defaultManifestPath = Path.Combine(projectDirectory, "..", "manifest.json");

        var translationPathOption = new Option<string>(
            "--translation-path",
            "Path to the translations folder. Defaults to <repo-root>/translations.")
        {
            IsRequired = false
        };
        translationPathOption.SetDefaultValue(defaultTranslationPath);

        var manifestPathOption = new Option<string>(
            "--manifest-path",
            "Path where manifest.json will be written. Defaults to <repo-root>/manifest.json.")
        {
            IsRequired = false
        };
        manifestPathOption.SetDefaultValue(defaultManifestPath);

        var command = new Command("generate-manifest", "Generate the manifest.json from translation files")
        {
            translationPathOption,
            manifestPathOption,
        };

        command.SetHandler(async (translationPath, manifestPath) =>
        {
            using var scope = serviceProvider.CreateScope();
            var generator = scope.ServiceProvider.GetRequiredService<ManifestGenerator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting manifest generation...");

            var success = await generator.GenerateManifest(translationPath, manifestPath);

            if (!success)
            {
                logger.LogError("Failed to generate manifest");
                Environment.Exit(1);
            }

            logger.LogInformation("Manifest generated successfully at {manifestPath}", manifestPath);
        }, translationPathOption, manifestPathOption);

        return command;
    }

    private static string ResolveProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BTCPayTranslator.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate the Translator project directory (BTCPayTranslator.csproj) " +
            "anywhere above AppContext.BaseDirectory.");
    }

}
