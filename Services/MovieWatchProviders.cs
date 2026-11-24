using System.Text.Json;

namespace AfterTheCredits.Services;

internal sealed record MovieWatchProviders(
    List<string> Streaming, List<string> Rental, List<string> Purchase)
{
    public static async Task<MovieWatchProviders> GetForIrelandAsync(
        HttpClient client, int movieId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"https://api.themoviedb.org/3/movie/{movieId}/watch/providers", cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.GetProperty("results").TryGetProperty("IE", out var ireland))
        {
            return new MovieWatchProviders([], [], []);
        }

        return new MovieWatchProviders(
            ReadNames(ireland, "flatrate"),
            ReadNames(ireland, "rent"),
            ReadNames(ireland, "buy"));
    }

    private static List<string> ReadNames(JsonElement country, string category)
    {
        if (!country.TryGetProperty(category, out var providers))
        {
            return [];
        }

        return providers.EnumerateArray()
            .Select(provider => provider.GetProperty("provider_name").GetString())
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
