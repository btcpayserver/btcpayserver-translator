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
                "anthropic/claude-3.6-sonnet";

        // Optimized for speed but still safe
        _semaphore = new SemaphoreSlim(2); // 2 concurrent requests max to avoid rate limits

        _logger.LogInformation("Fast Translation Service initialized - Model: {Model}", _model);
    }

    public async Task<TranslationResponse> TranslateAsync(TranslationRequest request)
    {
        var maxRetries = 3;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var strictMode = attempt > 1;
                var maxTokens = ComputeMaxTokens(request.SourceText);

                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { 
                            role = "system", 
                            content = BuildSystemPrompt(request.TargetLanguage, strictMode)
                        },
                        new { 
                            role = "user", 
                            content = request.SourceText
                        }
                    },
                    max_tokens = maxTokens,
                    temperature = 0.0
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
                        return new TranslationResponse(request.Key, string.Empty, false, 
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
                        return new TranslationResponse(request.Key, string.Empty, false, 
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
                        if (!IsValidTranslationOutput(request.SourceText, translatedText, out var reason))
                        {
                            _logger.LogWarning(
                                "Rejected suspicious translation for key '{Key}' (attempt {Attempt}/{MaxRetries}): {Reason}",
                                request.Key,
                                attempt,
                                maxRetries,
                                reason);

                            if (attempt == maxRetries)
                            {
                                return new TranslationResponse(request.Key, string.Empty, false, reason);
                            }

                            await Task.Delay(800);
                            continue;
                        }

                        return new TranslationResponse(request.Key, translatedText, true);
                    }
                }

                if (attempt == maxRetries)
                {
                    return new TranslationResponse(request.Key, string.Empty, false, 
                        "No translation returned");
                }
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    return new TranslationResponse(request.Key, string.Empty, false, ex.Message);
                }
                await Task.Delay(500); // Quick retry
            }
        }

        return new TranslationResponse(request.Key, string.Empty, false, "Translation failed");
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

    private static string BuildSystemPrompt(string targetLanguage, bool strictMode)
    {
        var strictRules = strictMode
            ? "\n\nSTRICT RETRY MODE: Your previous answer was invalid. Do not ask for more input. Return only the final translated UI string."
            : string.Empty;

        return $@"You are translating a single BTCPay Server UI string to {targetLanguage}.

Rules:
- Translate the full meaning faithfully. Do not summarize, simplify, or omit details.
- Keep the original tone and intent (for example, command labels remain short/imperative).
- Preserve placeholders exactly (examples: {{0}}, {{OrderId}}, {{InvoiceId}}).
- Preserve HTML tags/entities, punctuation, casing, and line breaks exactly.
- Keep technical/product names and standard crypto terms in English when commonly used.
- Do not translate to English unless the source is already English-only technical jargon.
- Never ask for more text or context.
- Never mention instructions, prompts, role, AI, or translation process.
- Output only the translated text for this one string, with no quotes or extra commentary.

Return only the translated string.{strictRules}";
    }

    private static int ComputeMaxTokens(string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
            return 220;

        // Approximate source tokens and allow expansion for longer target-language strings.
        var estimatedTokens = (int)Math.Ceiling((sourceText.Length / 4.0) * 2.0);
        var bounded = Math.Clamp(estimatedTokens, 220, 900);
        return bounded;
    }

    private static bool IsValidTranslationOutput(string sourceText, string translatedText, out string reason)
    {
        if (TranslationValidationRules.IsSuspiciousMetaResponse(translatedText))
        {
            reason = "Suspicious LLM/meta-response content";
            return false;
        }

        if (!TranslationValidationRules.HasMatchingPlaceholders(sourceText, translatedText))
        {
            reason = "Placeholder/token mismatch";
            return false;
        }

        if (TranslationValidationRules.IsLikelySentenceFallback(sourceText, translatedText))
        {
            reason = "Suspicious source fallback (sentence-like translation equals source text)";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
