using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BTCPayTranslator.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BTCPayTranslator.Services;

public class BaseTranslationService : ITranslationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BaseTranslationService> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly SemaphoreSlim _semaphore;

    public string ProviderName => "OpenRouter Fast";

    public BaseTranslationService(HttpClient httpClient, IConfiguration configuration, ILogger<BaseTranslationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // Get API key from environment variable
        _apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? 
                 configuration["TranslationService:OpenRouter:ApiKey"] ?? 
                 throw new ArgumentException("OpenRouter API key not found. Set OPENROUTER_API_KEY environment variable.");
        
        _model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? 
                configuration["TranslationService:OpenRouter:Model"] ?? 
                "anthropic/claude-3.5-sonnet";

        // Optimized for speed but still safe
        _semaphore = new SemaphoreSlim(2); // 2 concurrent requests max to avoid rate limits

        _logger.LogInformation("Fast Translation Service initialized - Model: {Model}", _model);
    }

    public async Task<TranslationResponse> TranslateAsync(TranslationRequest request)
    {
        var maxRetries = 2; // Reduced retries for speed
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Optimized prompt for faster processing
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { 
                            role = "system", 
                            content = $@"You are a professional translator for BTCPay Server, a Bitcoin payment processor.
Translate the given English text to {request.TargetLanguage}.

## Context
This text is UI content for a BTCPayServer payment system.
Your goal is to produce clear, professional, and user-friendly translations suitable for financial software.

## Guidelines

• Keep technical and cryptocurrency terms in their commonly used form, preferably using transliteration when appropriate.

• Retain key terms such as Bitcoin, Lightning, and other crypto-specific terms as-is or transliterated into the target language.

• Use a formal tone, appropriate for financial applications.

• Keep placeholder variables like {{0}}, {{1}} unchanged.

• Preserve HTML tags and special formatting as-is.

• Prefer transliteration over translation for standard UI terms unless there is a widely accepted translated equivalent.

• Ensure proper sentence structure according to the target language's grammar rules.

## Examples

| English Text | Hindi Translation | Spanish Translation | French Translation |
|--------------|-------------------|---------------------|-------------------|
| ""Hot wallet"" | ""हॉट वॉलेट"" | ""Hot wallet"" | ""Portefeuille chaud"" |
| ""Invoice"" | ""इनवॉइस"" | ""Factura"" | ""Facture"" |
| ""Settings"" | ""सेटिंग्स"" | ""Configuración"" | ""Paramètres"" |
| ""Payment successful"" | ""भुगतान सफल हुआ"" | ""Pago exitoso"" | ""Paiement réussi"" |


Edge Cases:

- If the term is widely used as-is in the target language (e.g., “Invoice”), prefer transliteration in non-English languages.
- If a clear translation exists and is commonly used (e.g., “Settings” → “Paramètres” in French), use the translated term.
- Do not translate placeholders or variables.
- Do not explain your translation — output only the final translated string.

Respond with only the translated text.
No explanations, no additional formatting, no comments."
                        },
                        new { 
                            role = "user", 
                            content = request.SourceText
                        }
                    },
                    max_tokens = 150, // Reduced for faster response
                    temperature = 0.0, // More deterministic
                    top_p = 0.9
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
                {
                    Content = content
                };

                // Essential headers only
                httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");
                httpRequest.Headers.Add("HTTP-Referer", "BTCPayTranslator");
                httpRequest.Headers.Add("X-Title", "BTCPayServer");

                var response = await _httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == maxRetries)
                    {
                        return new TranslationResponse(request.Key, request.SourceText, false, 
                            $"API error: {response.StatusCode}");
                    }
                    await Task.Delay(1000); // Quick retry delay
                    continue;
                }

                // Quick HTML check
                if (responseContent.TrimStart().StartsWith("<"))
                {
                    if (attempt == maxRetries)
                    {
                        return new TranslationResponse(request.Key, request.SourceText, false, 
                            "HTML error response");
                    }
                    await Task.Delay(1000);
                    continue;
                }

                // Fast JSON parsing
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                
                if (jsonResponse.TryGetProperty("choices", out var choices) && 
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement))
                {
                    var translatedText = contentElement.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(translatedText))
                    {
                        return new TranslationResponse(request.Key, translatedText, true);
                    }
                }

                if (attempt == maxRetries)
                {
                    return new TranslationResponse(request.Key, request.SourceText, false, 
                        "No translation returned");
                }
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    return new TranslationResponse(request.Key, request.SourceText, false, ex.Message);
                }
                await Task.Delay(500); // Quick retry
            }
        }

        return new TranslationResponse(request.Key, request.SourceText, false, "Translation failed");
    }

    public async Task<BatchTranslationResponse> TranslateBatchAsync(BatchTranslationRequest request)
    {
        var startTime = DateTime.UtcNow;
        var results = new List<TranslationResponse>();
        
        _logger.LogInformation("Starting FAST batch translation of {Count} items to {Language} with 2 concurrent requests", 
            request.Items.Count, request.TargetLanguage);

        // Process in parallel chunks for speed
        var chunks = ChunkItems(request.Items, 50); // Process 50 at a time
        var completedCount = 0;

        foreach (var chunk in chunks)
        {
            var chunkTasks = chunk.Select(async item =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    var translationRequest = new TranslationRequest(
                        item.Key,
                        item.SourceText,
                        request.TargetLanguage,
                        item.Context
                    );

                    var result = await TranslateAsync(translationRequest);
                    
                    // Log progress every 10 items
                    var currentCount = Interlocked.Increment(ref completedCount);
                    if (currentCount % 10 == 0)
                    {
                        _logger.LogInformation("Progress: {Current}/{Total} completed", currentCount, request.Items.Count);
                    }

                    return result;
                }
                finally
                {
                    _semaphore.Release();
                    // Small delay to avoid overwhelming the API
                    await Task.Delay(300); // Increased delay to avoid rate limits
                }
            });

            var chunkResults = await Task.WhenAll(chunkTasks);
            results.AddRange(chunkResults);

            // Brief pause between chunks
            if (chunks.Count() > 1)
            {
                await Task.Delay(500); // Half second between chunks
            }
        }

        var duration = DateTime.UtcNow - startTime;
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count - successCount;

        _logger.LogInformation("FAST batch translation completed: {SuccessCount}/{TotalCount} successful in {Duration:mm\\:ss}", 
            successCount, results.Count, duration);

        // Log some sample translations
        var successfulTranslations = results.Where(r => r.Success).Take(5);
        foreach (var translation in successfulTranslations)
        {
            _logger.LogInformation("Sample: '{Key}' -> '{Translation}'", 
                translation.Key, translation.TranslatedText);
        }

        // Log failures
        var failures = results.Where(r => !r.Success).Take(5);
        foreach (var failure in failures)
        {
            _logger.LogWarning("Failed: '{Key}' - {Error}", failure.Key, failure.Error);
        }

        return new BatchTranslationResponse(results, successCount, failureCount, duration);
    }

    private static IEnumerable<List<T>> ChunkItems<T>(List<T> items, int chunkSize)
    {
        for (int i = 0; i < items.Count; i += chunkSize)
        {
            yield return items.Skip(i).Take(chunkSize).ToList();
        }
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}
