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
            CreateCheckoutTranslateCommand(serviceProvider),
            CreateCheckoutBatchCommand(serviceProvider),
            CreateCheckoutStatusCommand(serviceProvider)
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

        // Register Fast OpenRouter translation service
        services.AddTransient<ITranslationService, BaseTranslationService>();
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

        var command = new Command("translate", "Translate BTCPay Server to a specific language")
        {
            languageOption,
            forceOption
        };

        command.SetHandler(async (language, force) =>
        {
            using var scope = serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<TranslationOrchestrator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting translation for language: {Language}", language);
            
            var success = await orchestrator.TranslateToLanguageAsync(language, force);
            
            if (success)
            {
                logger.LogInformation("Translation completed successfully!");
                Environment.Exit(0);
            }
            else
            {
                logger.LogError("Translation failed!");
                Environment.Exit(1);
            }
        }, languageOption, forceOption);

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

        var command = new Command("batch", "Translate BTCPay Server to multiple languages")
        {
            languagesOption,
            forceOption,
            continueOnErrorOption
        };

        command.SetHandler(async (languages, force, continueOnError) =>
        {
            using var scope = serviceProvider.CreateScope();
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
            
            Environment.Exit(successCount == totalCount ? 0 : 1);
        }, languagesOption, forceOption, continueOnErrorOption);

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

    private static Command CreateCheckoutTranslateCommand(ServiceProvider serviceProvider)
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

        var command = new Command("checkout-translate", "Translate BTCPay Server checkout to a specific language")
        {
            languageOption,
            forceOption
        };

        command.SetHandler(async (language, force) =>
        {
            using var scope = serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<TranslationOrchestrator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting checkout translation for language: {Language}", language);
            
            var success = await orchestrator.TranslateCheckoutToLanguageAsync(language, force);
            
            if (success)
            {
                logger.LogInformation("Checkout translation completed successfully!");
                Environment.Exit(0);
            }
            else
            {
                logger.LogError("Checkout translation failed!");
                Environment.Exit(1);
            }
        }, languageOption, forceOption);

        return command;
    }

    private static Command CreateCheckoutBatchCommand(ServiceProvider serviceProvider)
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

        var command = new Command("checkout-batch", "Translate BTCPay Server checkout to multiple languages")
        {
            languagesOption,
            forceOption,
            continueOnErrorOption
        };

        command.SetHandler(async (languages, force, continueOnError) =>
        {
            using var scope = serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<TranslationOrchestrator>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting batch checkout translation for languages: {Languages}", 
                string.Join(", ", languages));
            
            var results = await orchestrator.TranslateCheckoutToMultipleLanguagesAsync(languages, force, continueOnError);
            
            var successCount = results.Values.Count(success => success);
            var totalCount = results.Count;
            
            logger.LogInformation("Batch checkout translation completed: {SuccessCount}/{TotalCount} successful", 
                successCount, totalCount);
                
            foreach (var result in results)
            {
                var status = result.Value ? "✓" : "✗";
                logger.LogInformation("  {Status} {Language}", status, result.Key);
            }
            
            Environment.Exit(successCount == totalCount ? 0 : 1);
        }, languagesOption, forceOption, continueOnErrorOption);

        return command;
    }

    private static Command CreateCheckoutStatusCommand(ServiceProvider serviceProvider)
    {
        var command = new Command("checkout-status", "Show checkout translation status for all languages");

        command.SetHandler(async () =>
        {
            using var scope = serviceProvider.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var fileWriter = scope.ServiceProvider.GetRequiredService<FileWriter>();
            
            var outputDir = configuration["CheckoutTranslation:OutputDirectory"] ?? 
                           "checkoutTranslations";

            Console.WriteLine("Checkout Translation Status:");
            Console.WriteLine("============================");
            Console.WriteLine($"{"Language",-15} {"Code",-10} {"File Exists",-12} {"Translations",-12}");
            Console.WriteLine(new string('-', 55));

            foreach (var lang in SupportedLanguages.GetAllLanguages().OrderBy(l => l.Name))
            {
                var filePath = Path.Combine(outputDir, $"{lang.Code}.json");
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
}
