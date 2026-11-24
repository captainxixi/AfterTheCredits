using System.Net.Http.Json;
using System.Text.Json;

namespace AfterTheCredits.Services;

public sealed class GeminiEmbeddingService(HttpClient httpClient, IConfiguration configuration)
{
    internal const string Model = "gemini-embedding-2";
    internal const int Dimensions = 768;

    public async Task<float[]> GenerateEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var apiKey = configuration["Gemini:ApiKey"] ?? configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Configure Gemini:ApiKey before generating embeddings.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:embedContent");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            content = new { parts = new[] { new { text } } },
            outputDimensionality = Dimensions
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream, cancellationToken: cancellationToken);
        var values = document.RootElement.GetProperty("embedding").GetProperty("values");
        var vector = values.EnumerateArray().Select(value => value.GetSingle()).ToArray();

        if (vector.Length != Dimensions || vector.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidDataException("Gemini returned an invalid embedding vector.");
        }

        return vector;
    }
}
