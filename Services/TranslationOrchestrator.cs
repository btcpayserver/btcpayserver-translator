using System.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using BTCPayTranslator.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BTCPayTranslator.Services;

public class TranslationOrchestrator
{
    private readonly ITranslationService _translationService;
    private readonly TranslationExtractor _extractor;
    private readonly FileWriter _fileWriter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TranslationOrchestrator> _logger;

    public TranslationOrchestrator(
        ITranslationService translationService,
        TranslationExtractor extractor,
        FileWriter fileWriter,
        IConfiguration configuration,
        ILogger<TranslationOrchestrator> logger)
    {
        _translationService = translationService;
        _extractor = extractor;
        _fileWriter = fileWriter;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> TranslateToLanguageAsync(string languageCode, bool forceRetranslate = false)
    {
        try
        {
            var languageInfo = SupportedLanguages.GetLanguageInfo(languageCode);
            if (languageInfo == null)
            {
                _logger.LogError("Unsupported language code: {LanguageCode}", languageCode);
                return false;
            }

            _logger.LogInformation("Starting translation to {Language} ({NativeName})", 
                languageInfo.Name, languageInfo.NativeName);

            // Extract source translations from Default.cs
            var inputFile = _configuration["Translation:InputFile"] ?? 
                           "../BTCPayServer/Services/Translations.Default.cs";
            var sourceTranslations = await _extractor.ExtractFromDefaultFileAsync(inputFile);

            // Determine output paths
            var outputDir = _configuration["Translation:OutputDirectory"] ?? 
                           "../BTCPayServer/translations";
            var outputPath = Path.Combine(outputDir, $"{languageInfo.Name.ToLower()}.json");

            // Load existing translations if they exist
            var existingTranslations = await _fileWriter.LoadExistingBackendTranslationsAsync(outputPath);

            // Determine what needs to be translated
            Dictionary<string, string> translationsToProcess;
            if (forceRetranslate)
            {
                translationsToProcess = sourceTranslations;
                _logger.LogInformation("Force retranslate mode: processing all {Count} translations", 
                    sourceTranslations.Count);
            }
            else
            {
                translationsToProcess = _extractor.GetTranslationsToUpdate(sourceTranslations, existingTranslations);
                if (translationsToProcess.Count == 0)
                {
                    _logger.LogInformation("No new translations needed for {Language}", languageInfo.Name);
                    return true;
                }
            }

            // Prepare translation requests for ALL translations
            var batchSize = _configuration.GetValue<int>("Translation:BatchSize", 50);
            var requests = translationsToProcess
                .Select(t => new TranslationRequest(t.Key, t.Value, languageInfo.Name))
                .ToList();

            // Process translations in batches
            var allResults = new List<TranslationResponse>();
            for (int i = 0; i < requests.Count; i += batchSize)
            {
                var batch = requests.Skip(i).Take(batchSize).ToList();
                _logger.LogInformation("Processing batch {CurrentBatch}/{TotalBatches} ({Count} items)", 
                    (i / batchSize) + 1, (int)Math.Ceiling((double)requests.Count / batchSize), batch.Count);

                var batchRequest = new BatchTranslationRequest(batch, languageInfo.Name, languageInfo.NativeName);
                var batchResponse = await _translationService.TranslateBatchAsync(batchRequest);
                allResults.AddRange(batchResponse.Results);

                // Add delay between batches to be respectful to the API
                if (i + batchSize < requests.Count)
                {
                    var delay = _configuration.GetValue<int>("Translation:DelayBetweenRequests", 1000);
                    await Task.Delay(delay);
                }
            }

            // Process results
            var newTranslations = allResults
                .Where(r => r.Success)
                .ToDictionary(r => r.Key, r => r.TranslatedText);

            var finalTranslations = _extractor.MergeTranslations(existingTranslations, newTranslations);

            // Write backend translation file (simple JSON format)
            await _fileWriter.WriteBackendTranslationFileAsync(
                outputPath, languageInfo, finalTranslations);

            // Write summary report
            var summaryResponse = new BatchTranslationResponse(
                allResults, 
                allResults.Count(r => r.Success), 
                allResults.Count(r => !r.Success),
                TimeSpan.Zero);

            await _fileWriter.WriteSummaryReportAsync(
                outputPath, languageInfo.Name, summaryResponse, finalTranslations);

            var successRate = (double)newTranslations.Count / translationsToProcess.Count * 100;
            _logger.LogInformation(
                "Translation completed for {Language}: {SuccessCount}/{TotalCount} successful ({SuccessRate:F1}%)",
                languageInfo.Name, newTranslations.Count, translationsToProcess.Count, successRate);

            return successRate > 80; // Consider successful if >80% success rate
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during translation process for language {LanguageCode}", languageCode);
            return false;
        }
    }

    public async Task<Dictionary<string, bool>> TranslateToMultipleLanguagesAsync(
        IEnumerable<string> languageCodes,
        bool forceRetranslate = false,
        bool continueOnError = true)
    {
        var results = new Dictionary<string, bool>();

        foreach (var languageCode in languageCodes)
        {
            try
            {
                _logger.LogInformation("Starting translation for language: {LanguageCode}", languageCode);
                var success = await TranslateToLanguageAsync(languageCode, forceRetranslate);
                results[languageCode] = success;

                if (!success && !continueOnError)
                {
                    _logger.LogWarning("Translation failed for {LanguageCode}, stopping batch process", languageCode);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating language {LanguageCode}", languageCode);
                results[languageCode] = false;

                if (!continueOnError)
                {
                    break;
                }
            }
        }

        var totalLanguages = results.Count;
        var successfulLanguages = results.Values.Count(success => success);
        _logger.LogInformation("Batch translation completed: {SuccessCount}/{TotalCount} languages successful",
            successfulLanguages, totalLanguages);

        return results;
    }

    public async Task<bool> TranslateCheckoutToLanguageAsync(string languageCode, bool forceRetranslate = false)
    {
        try
        {
            var languageInfo = SupportedLanguages.GetLanguageInfo(languageCode);
            if (languageInfo == null)
            {
                _logger.LogError("Unsupported language code: {LanguageCode}", languageCode);
                return false;
            }

            _logger.LogInformation("Starting checkout translation to {Language} ({NativeName})", 
                languageInfo.Name, languageInfo.NativeName);

            // Extract source checkout translations from en.json
            var inputFile = _configuration["CheckoutTranslation:InputFile"] ?? 
                           "https://raw.githubusercontent.com/btcpayserver/btcpayserver/master/BTCPayServer/wwwroot/locales/checkout/en.json";
            var sourceTranslations = await _extractor.ExtractFromCheckoutJsonAsync(inputFile);

            // Determine output paths
            var outputDir = _configuration["CheckoutTranslation:OutputDirectory"] ?? 
                           "checkoutTranslations";
            var outputPath = Path.Combine(outputDir, $"{languageInfo.Code}.json");

            // Load existing translations if they exist
            var existingTranslations = await _fileWriter.LoadExistingBackendTranslationsAsync(outputPath);

            // Determine what needs to be translated
            Dictionary<string, string> translationsToProcess;
            if (forceRetranslate)
            {
                translationsToProcess = sourceTranslations;
                _logger.LogInformation("Force retranslate mode: processing all {Count} checkout translations", 
                    sourceTranslations.Count);
            }
            else
            {
                translationsToProcess = _extractor.GetTranslationsToUpdate(sourceTranslations, existingTranslations);
                if (translationsToProcess.Count == 0)
                {
                    _logger.LogInformation("No new checkout translations needed for {Language}", languageInfo.Name);
                    return true;
                }
            }

            // Prepare translation requests for ALL translations
            var batchSize = _configuration.GetValue<int>("CheckoutTranslation:BatchSize", 40);
            var requests = translationsToProcess
                .Select(t => new TranslationRequest(t.Key, t.Value, languageInfo.Name))
                .ToList();

            // Process translations in batches
            var allResults = new List<TranslationResponse>();
            for (int i = 0; i < requests.Count; i += batchSize)
            {
                var batch = requests.Skip(i).Take(batchSize).ToList();
                _logger.LogInformation("Processing checkout batch {CurrentBatch}/{TotalBatches} ({Count} items)", 
                    (i / batchSize) + 1, (int)Math.Ceiling((double)requests.Count / batchSize), batch.Count);

                var batchRequest = new BatchTranslationRequest(batch, languageInfo.Name, languageInfo.NativeName);
                var batchResponse = await _translationService.TranslateBatchAsync(batchRequest);
                allResults.AddRange(batchResponse.Results);

                // Add delay between batches to be respectful to the API
                if (i + batchSize < requests.Count)
                {
                    var delay = _configuration.GetValue<int>("CheckoutTranslation:DelayBetweenRequests", 1000);
                    await Task.Delay(delay);
                }
            }

            // Process results
            var newTranslations = allResults
                .Where(r => r.Success)
                .ToDictionary(r => r.Key, r => r.TranslatedText);

            var finalTranslations = _extractor.MergeTranslations(existingTranslations, newTranslations);

            // Write checkout translation file (with metadata)
            await _fileWriter.WriteCheckoutTranslationFileAsync(
                outputPath, languageInfo, finalTranslations);

            // Write summary report
            var summaryResponse = new BatchTranslationResponse(
                allResults, 
                allResults.Count(r => r.Success), 
                allResults.Count(r => !r.Success),
                TimeSpan.Zero);

            await _fileWriter.WriteSummaryReportAsync(
                outputPath, languageInfo.Name, summaryResponse, finalTranslations);

            var successRate = (double)newTranslations.Count / translationsToProcess.Count * 100;
            _logger.LogInformation(
                "Checkout translation completed for {Language}: {SuccessCount}/{TotalCount} successful ({SuccessRate:F1}%)",
                languageInfo.Name, newTranslations.Count, translationsToProcess.Count, successRate);

            return successRate > 80; // Consider successful if >80% success rate
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during checkout translation process for language {LanguageCode}", languageCode);
            return false;
        }
    }

    public async Task<Dictionary<string, bool>> TranslateCheckoutToMultipleLanguagesAsync(
        IEnumerable<string> languageCodes,
        bool forceRetranslate = false,
        bool continueOnError = true)
    {
        var results = new Dictionary<string, bool>();

        foreach (var languageCode in languageCodes)
        {
            try
            {
                _logger.LogInformation("Starting checkout translation for language: {LanguageCode}", languageCode);
                var success = await TranslateCheckoutToLanguageAsync(languageCode, forceRetranslate);
                results[languageCode] = success;

                if (!success && !continueOnError)
                {
                    _logger.LogWarning("Checkout translation failed for {LanguageCode}, stopping batch process", languageCode);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating checkout for language {LanguageCode}", languageCode);
                results[languageCode] = false;

                if (!continueOnError)
                {
                    break;
                }
            }
        }

        var totalLanguages = results.Count;
        var successfulLanguages = results.Values.Count(success => success);
        _logger.LogInformation("Batch checkout translation completed: {SuccessCount}/{TotalCount} languages successful",
            successfulLanguages, totalLanguages);

        return results;
    }

}
