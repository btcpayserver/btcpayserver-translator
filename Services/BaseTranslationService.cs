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

- For cryptocurrency and blockchain-specific terms (Bitcoin, Lightning, wallet types, etc.): use transliteration into the target language's script, or keep the English term if no transliteration is natural.
- For standard UI terms (Settings, Invoice, Dashboard, etc.): use the officially accepted translation in the target language if one exists and is widely used. Otherwise, transliterate.
- Use a formal tone, appropriate for financial applications.
- Keep placeholder variables like {{0}}, {{1}} unchanged.
- Preserve HTML tags and special formatting as-is.
- Never translate a term literally word-by-word if the result is unnatural or unused in the target language.
- Ensure proper sentence structure according to the target language's grammar rules.

## English Translation Examples

- ""Hot wallet"" -> Hindi: ""हॉट वॉलेट"" | Spanish: ""Hot wallet"" | French: ""Hot wallet""
- ""Invoice"" -> Hindi: ""इनवॉइस"" | Spanish: ""Factura"" | French: ""Facture""
- ""Settings"" -> Hindi: ""सेटिंग्स"" | Spanish: ""Configuración"" | French: ""Paramètres""
- ""Payment successful"" -> Hindi: ""भुगतान सफल हुआ"" | Spanish: ""Pago exitoso"" | French: ""Paiement réussi""

Respond with only the translated text.
No explanations, no additional formatting, no comments."
                        },
                        new { 
                            role = "user", 
                            content = request.SourceText
                        }
                    },
                    max_tokens = 400, // Reduced for faster response
                    temperature = 0.0 // More deterministic
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

    private async Task<List<TranslationResponse>> TranslateGroupAsync(
        List<TranslationRequest> group, int maxRetries = 2)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var targetLanguage = group[0].TargetLanguage;

                // Build numbered list: "1. text\n2. text\n..."
                var userContent = string.Join("\n", group.Select((r, i) => $"{i + 1}. {r.SourceText}"));

                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new {
                            role = "system",
                            content = $@"You are a professional translator for BTCPay Server, a Bitcoin payment processor.
Translate each numbered English string to {targetLanguage}.

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

- If the term is widely used as-is in the target language (e.g., ""Invoice""), prefer transliteration in non-English languages.
- If a clear translation exists and is commonly used (e.g., ""Settings"" → ""Paramètres"" in French), use the translated term.
- Do not translate placeholders or variables.
- Do not explain your translation — output only the final translated strings.

Respond with ONLY a numbered list matching the input order, one translation per line.
Format exactly: ""1. <translation>\n2. <translation>\n..."" — no extra text, no blank lines between entries."
                        },
                        new {
                            role = "user",
                            content = userContent
                        }
                    },
                    max_tokens = group.Count * 60 + 50,
                    temperature = 0.0,
                    top_p = 0.9
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
                {
                    Content = content
                };

                httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");
                httpRequest.Headers.Add("HTTP-Referer", "BTCPayTranslator");
                httpRequest.Headers.Add("X-Title", "BTCPayServer");

                var response = await _httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == maxRetries)
                        break;
                    await Task.Delay(1000);
                    continue;
                }

                if (responseContent.TrimStart().StartsWith("<"))
                {
                    if (attempt == maxRetries)
                        break;
                    await Task.Delay(1000);
                    continue;
                }

                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                if (jsonResponse.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement))
                {
                    var rawText = contentElement.GetString()?.Trim() ?? "";
                    var lines = rawText
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

                    // Parse "N. translation" lines
                    var parsed = new Dictionary<int, string>();
                    foreach (var line in lines)
                    {
                        var dotIndex = line.IndexOf('.');
                        if (dotIndex > 0 && int.TryParse(line[..dotIndex].Trim(), out var num))
                        {
                            parsed[num] = line[(dotIndex + 1)..].Trim();
                        }
                    }

                    // If we got enough lines back, return results
                    if (parsed.Count >= group.Count)
                    {
                        return group.Select((req, i) =>
                            parsed.TryGetValue(i + 1, out var t) && !string.IsNullOrEmpty(t)
                                ? new TranslationResponse(req.Key, t, true)
                                : new TranslationResponse(req.Key, req.SourceText, false, "Missing in group response")
                        ).ToList();
                    }

                    // Partial response — fall back to individually retrying missing ones
                    _logger.LogWarning("Group response had {Got}/{Expected} lines, retrying missing items individually",
                        parsed.Count, group.Count);

                    var fallbackResults = new List<TranslationResponse>();
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (parsed.TryGetValue(i + 1, out var t) && !string.IsNullOrEmpty(t))
                        {
                            fallbackResults.Add(new TranslationResponse(group[i].Key, t, true));
                        }
                        else
                        {
                            fallbackResults.Add(await TranslateAsync(group[i]));
                        }
                    }
                    return fallbackResults;
                }

                if (attempt == maxRetries)
                    break;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    _logger.LogWarning(ex, "Group translation failed, falling back to individual translation");
                    break;
                }
                await Task.Delay(500);
            }
        }

        // Full fallback: translate each item individually
        var individualResults = new List<TranslationResponse>();
        foreach (var req in group)
            individualResults.Add(await TranslateAsync(req));
        return individualResults;
    }

    public async Task<BatchTranslationResponse> TranslateBatchAsync(BatchTranslationRequest request)
    {
        var startTime = DateTime.UtcNow;
        var results = new List<TranslationResponse>();
        
        const int groupSize = 30; // strings per API call
        _logger.LogInformation("Starting batch translation of {Count} items to {Language} in groups of {GroupSize}", 
            request.Items.Count, request.TargetLanguage, groupSize);

        // Split all items into groups of groupSize; process groups concurrently (semaphore-limited)
        var groups = ChunkItems(request.Items, groupSize).ToList();
        var completedCount = 0;

        var groupTasks = groups.Select(async group =>
        {
            await _semaphore.WaitAsync();
            try
            {
                var translationRequests = group
                    .Select(item => new TranslationRequest(item.Key, item.SourceText, request.TargetLanguage, item.Context))
                    .ToList();

                var groupResults = await TranslateGroupAsync(translationRequests);

                var currentCount = Interlocked.Add(ref completedCount, group.Count);
                _logger.LogInformation("Progress: {Current}/{Total} completed", currentCount, request.Items.Count);

                return groupResults;
            }
            finally
            {
                _semaphore.Release();
            }
        });

        var allGroupResults = await Task.WhenAll(groupTasks);
        foreach (var groupResult in allGroupResults)
            results.AddRange(groupResult);

        var duration = DateTime.UtcNow - startTime;
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count - successCount;

        _logger.LogInformation("Batch translation completed: {SuccessCount}/{TotalCount} successful in {Duration:mm\\:ss}", 
            successCount, results.Count, duration);

        var successfulTranslations = results.Where(r => r.Success).Take(5);
        foreach (var translation in successfulTranslations)
        {
            _logger.LogInformation("Sample: '{Key}' -> '{Translation}'", 
                translation.Key, translation.TranslatedText);
        }

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
