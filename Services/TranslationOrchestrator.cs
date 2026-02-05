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

    public async Task<bool> UpdateLanguageAsync(string languageCode)
    {
        try
        {
            var languageInfo = SupportedLanguages.GetLanguageInfo(languageCode);
            if (languageInfo == null)
            {
                _logger.LogError("Unsupported language code: {LanguageCode}", languageCode);
                return false;
            }

            _logger.LogInformation("Starting update for {Language} ({NativeName})", 
                languageInfo.Name, languageInfo.NativeName);

            // Extract source translations from GitHub
            var inputFile = _configuration["Translation:InputFile"] ?? 
                           "https://raw.githubusercontent.com/btcpayserver/btcpayserver/master/BTCPayServer/Services/Translations.Default.cs";
            
            _logger.LogInformation("Fetching latest translations from GitHub...");
            var sourceTranslations = await _extractor.ExtractFromDefaultFileAsync(inputFile);
            _logger.LogInformation("Found {Count} strings in source", sourceTranslations.Count);

            // Determine output path
            var outputDir = _configuration["Translation:OutputDirectory"] ?? "translations";
            var outputPath = Path.Combine(outputDir, $"{languageInfo.Name.ToLower()}.json");

            // Load existing translations
            if (!File.Exists(outputPath))
            {
                _logger.LogError("Translation file not found: {OutputPath}. Use 'translate' command to create it first.", outputPath);
                return false;
            }

            var existingTranslations = await _fileWriter.LoadExistingBackendTranslationsAsync(outputPath);
            _logger.LogInformation("Loaded {Count} existing translations", existingTranslations.Count);

            // Find what's new, what's deleted, and what's unchanged
            var newKeys = sourceTranslations.Keys.Except(existingTranslations.Keys).ToList();
            var deletedKeys = existingTranslations.Keys.Except(sourceTranslations.Keys).ToList();
            var unchangedKeys = existingTranslations.Keys.Intersect(sourceTranslations.Keys).ToList();

            _logger.LogInformation("Analysis: {NewCount} new strings, {DeletedCount} deleted strings, {UnchangedCount} unchanged strings",
                newKeys.Count, deletedKeys.Count, unchangedKeys.Count);

            if (newKeys.Count == 0 && deletedKeys.Count == 0)
            {
                _logger.LogInformation("No updates needed. Translation file is up to date.");
                return true;
            }

            // Translate only new strings
            var translationsToProcess = newKeys.ToDictionary(k => k, k => sourceTranslations[k]);
            
            if (translationsToProcess.Count > 0)
            {
                _logger.LogInformation("Translating {Count} new strings...", translationsToProcess.Count);

                var batchSize = _configuration.GetValue<int>("Translation:BatchSize", 50);
                var requests = translationsToProcess
                    .Select(t => new TranslationRequest(t.Key, t.Value, languageInfo.Name))
                    .ToList();

                var allResults = new List<TranslationResponse>();
                for (int i = 0; i < requests.Count; i += batchSize)
                {
                    var batch = requests.Skip(i).Take(batchSize).ToList();
                    _logger.LogInformation("Processing batch {CurrentBatch}/{TotalBatches} ({Count} items)", 
                        (i / batchSize) + 1, (int)Math.Ceiling((double)requests.Count / batchSize), batch.Count);

                    var batchRequest = new BatchTranslationRequest(batch, languageInfo.Name, languageInfo.NativeName);
                    var batchResponse = await _translationService.TranslateBatchAsync(batchRequest);
                    allResults.AddRange(batchResponse.Results);

                    if (i + batchSize < requests.Count)
                    {
                        var delay = _configuration.GetValue<int>("Translation:DelayBetweenRequests", 1000);
                        await Task.Delay(delay);
                    }
                }

                var newTranslations = allResults
                    .Where(r => r.Success)
                    .ToDictionary(r => r.Key, r => r.TranslatedText);

                _logger.LogInformation("Successfully translated {SuccessCount}/{TotalCount} new strings",
                    newTranslations.Count, translationsToProcess.Count);

                // Merge new translations with existing ones
                foreach (var newTranslation in newTranslations)
                {
                    existingTranslations[newTranslation.Key] = newTranslation.Value;
                }
            }

            // Remove deleted keys
            foreach (var deletedKey in deletedKeys)
            {
                existingTranslations.Remove(deletedKey);
                _logger.LogDebug("Removed deleted key: {Key}", deletedKey);
            }

            // Rebuild the final dictionary in the same order as source
            var finalTranslations = new Dictionary<string, string>();
            foreach (var sourceKey in sourceTranslations.Keys)
            {
                if (existingTranslations.ContainsKey(sourceKey))
                {
                    finalTranslations[sourceKey] = existingTranslations[sourceKey];
                }
            }

            // Write updated translation file
            await _fileWriter.WriteBackendTranslationFileAsync(
                outputPath, languageInfo, finalTranslations);

            _logger.LogInformation(
                "Update completed for {Language}: {TotalCount} total strings ({NewCount} added, {DeletedCount} removed)",
                languageInfo.Name, finalTranslations.Count, newKeys.Count, deletedKeys.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during update process for language {LanguageCode}", languageCode);
            return false;
        }
    }

    public async Task<Dictionary<string, bool>> UpdateMultipleLanguagesAsync(
        IEnumerable<string> languageCodes,
        bool continueOnError = true)
    {
        var results = new Dictionary<string, bool>();

        foreach (var languageCode in languageCodes)
        {
            try
            {
                _logger.LogInformation("Starting update for language: {LanguageCode}", languageCode);
                var success = await UpdateLanguageAsync(languageCode);
                results[languageCode] = success;

                if (!success && !continueOnError)
                {
                    _logger.LogWarning("Update failed for {LanguageCode}, stopping batch process", languageCode);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating language {LanguageCode}", languageCode);
                results[languageCode] = false;

                if (!continueOnError)
                {
                    break;
                }
            }
        }

        var totalLanguages = results.Count;
        var successfulLanguages = results.Values.Count(success => success);
        _logger.LogInformation("Batch update completed: {SuccessCount}/{TotalCount} languages successful",
            successfulLanguages, totalLanguages);

        return results;
    }

}
