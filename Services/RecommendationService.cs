using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AfterTheCredits.Data;
using AfterTheCredits.Models;
using Microsoft.EntityFrameworkCore;

namespace AfterTheCredits.Services;

public sealed record MovieCandidate(
    Movie Movie,
    bool IsFromWatchlist,
    string Overview,
    IReadOnlyList<int> GenreIds,
    int VoteCount)
{
    public IReadOnlyList<string> GenreNames { get; init; } = Array.Empty<string>();

    public string EmbeddingText
    {
        get
        {
            var lines = new List<string> { $"Title: {Movie.Title.Trim()}" };

            if (GenreNames.Count > 0)
            {
                lines.Add($"Genres: {string.Join(", ", GenreNames)}");
            }

            if (!string.IsNullOrWhiteSpace(Overview))
            {
                lines.Add($"Overview: {Overview.Trim()}");
            }

            return string.Join("\n", lines);
        }
    }
}

public sealed record RankedMovieCandidate(
    MovieCandidate Candidate, double Similarity, MovieCandidate ClosestReference);

public class RecommendationService
{
    internal const int FavoriteTarget = 5;
    internal const double FavoriteRating = 8;

    private readonly AppDbContext _dbContext;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly GeminiEmbeddingService _gemini;

    public RecommendationService(
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        GeminiEmbeddingService gemini)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _gemini = gemini;
    }

    public async Task<IReadOnlyList<MovieCandidate>> GenerateCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        var (watchedIds, unwatchedWatchlistIds) =
            await GetWatchlistMovieIdsAsync(cancellationToken);

        using var client = CreateTmdbClient();

        using var popularResponse = await client.GetAsync(
            "https://api.themoviedb.org/3/movie/popular", cancellationToken);
        popularResponse.EnsureSuccessStatusCode();

        using var popularStream = await popularResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var popularDocument = await JsonDocument.ParseAsync(
            popularStream, cancellationToken: cancellationToken);

        var candidates = new Dictionary<int, MovieCandidate>();
        foreach (var result in popularDocument.RootElement.GetProperty("results").EnumerateArray())
        {
            var movieId = result.GetProperty("id").GetInt32();
            if (watchedIds.Contains(movieId) || candidates.ContainsKey(movieId))
            {
                continue;
            }

            candidates.Add(movieId, ReadCandidate(result, unwatchedWatchlistIds.Contains(movieId)));
        }

        foreach (var movieId in unwatchedWatchlistIds)
        {
            if (candidates.ContainsKey(movieId))
            {
                continue;
            }

            using var response = await client.GetAsync(
                $"https://api.themoviedb.org/3/movie/{movieId}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream, cancellationToken: cancellationToken);

            candidates.Add(movieId, ReadCandidate(document.RootElement, isFromWatchlist: true));
        }

        if (!candidates.Values.Any(candidate => candidate.GenreIds.Count > 0))
        {
            return candidates.Values.ToList();
        }

        var genreNamesById = await GetGenreNamesByIdAsync(client, cancellationToken);
        return candidates.Values.Select(candidate => candidate with
        {
            GenreNames = candidate.GenreIds
                .Where(genreNamesById.ContainsKey)
                .Select(genreId => genreNamesById[genreId])
                .ToArray()
        }).ToList();
    }

    public async Task<IReadOnlyList<RankedMovieCandidate>> RankCandidatesAsync(
        IReadOnlyList<MovieCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
        {
            return Array.Empty<RankedMovieCandidate>();
        }

        var referenceIds = await _dbContext.WatchlistEntries
            .Where(entry => entry.IsWatched && entry.MyRating >= FavoriteRating)
            .GroupBy(entry => entry.MovieId)
            .Select(group => new { MovieId = group.Key, Rating = group.Max(entry => entry.MyRating) })
            .OrderByDescending(entry => entry.Rating)
            .ThenBy(entry => entry.MovieId)
            .Select(entry => entry.MovieId)
            .Take(FavoriteTarget)
            .ToListAsync(cancellationToken);
        if (referenceIds.Count == 0)
        {
            return Array.Empty<RankedMovieCandidate>();
        }

        using var client = CreateTmdbClient();
        var references = new List<(MovieCandidate Movie, float[] Vector)>(referenceIds.Count);
        foreach (var referenceId in referenceIds)
        {
            using var response = await client.GetAsync(
                $"https://api.themoviedb.org/3/movie/{referenceId}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var reference = ReadCandidate(document.RootElement, isFromWatchlist: true) with
            {
                GenreNames = document.RootElement.GetProperty("genres").EnumerateArray()
                    .Select(genre => genre.GetProperty("name").GetString())
                    .OfType<string>()
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray()
            };

            var vector = await GetOrCreateEmbeddingAsync(reference.EmbeddingText, cancellationToken);
            references.Add((reference, vector));
        }

        if (references.Count == 0)
        {
            return Array.Empty<RankedMovieCandidate>();
        }

        var ranked = new List<RankedMovieCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var vector = await GetOrCreateEmbeddingAsync(
                candidate.EmbeddingText, cancellationToken);
            var matches = references
                .Select(reference => (reference.Movie, Score: CosineSimilarity(reference.Vector, vector)))
                .ToList();
            var closest = matches.MaxBy(match => match.Score);
            var similarity = matches.Average(match => match.Score);
            ranked.Add(new RankedMovieCandidate(candidate, similarity, closest.Movie));
        }

        return ranked.OrderByDescending(result => result.Similarity).ToList();
    }

    public async Task<(MovieCandidate Movie, string Genre)?> FindWildCardAsync(
        IReadOnlySet<int> personalizedIds, CancellationToken cancellationToken = default)
    {
        var (watchedIds, unwatchedWatchlistIds) = await GetWatchlistMovieIdsAsync(cancellationToken);
        if (watchedIds.Count == 0)
        {
            return null;
        }

        using var client = CreateTmdbClient();
        var watchedGenreCounts = new Dictionary<int, int>();
        var moviesWithGenres = 0;
        foreach (var id in watchedIds)
        {
            using var details = await client.GetAsync(
                $"https://api.themoviedb.org/3/movie/{id}", cancellationToken);
            if (details.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            details.EnsureSuccessStatusCode();
            using var detailsStream = await details.Content.ReadAsStreamAsync(cancellationToken);
            using var detailsJson = await JsonDocument.ParseAsync(
                detailsStream, cancellationToken: cancellationToken);
            var genres = ReadGenreIds(detailsJson.RootElement);
            if (genres.Count == 0)
            {
                continue;
            }

            moviesWithGenres++;
            foreach (var genreId in genres.Distinct())
            {
                watchedGenreCounts[genreId] = watchedGenreCounts.GetValueOrDefault(genreId) + 1;
            }
        }

        if (moviesWithGenres == 0)
        {
            return null;
        }

        var namesById = await GetGenreNamesByIdAsync(client, cancellationToken);
        var rareGenreIds = namesById.Keys
            .Where(id => watchedGenreCounts.GetValueOrDefault(id) <= moviesWithGenres / 5)
            .ToHashSet();
        if (rareGenreIds.Count == 0)
        {
            return null;
        }

        var included = Uri.EscapeDataString(string.Join("|", rareGenreIds));
        var excluded = string.Join(",", namesById.Keys.Except(rareGenreIds));
        var url = "https://api.themoviedb.org/3/discover/movie" +
            $"?with_genres={included}&vote_average.gte=7&vote_count.gte=100&sort_by=vote_count.desc";
        if (excluded.Length > 0)
        {
            url += $"&without_genres={excluded}";
        }

        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var wildCard = json.RootElement.GetProperty("results").EnumerateArray()
            .Select(movie => ReadCandidate(movie, unwatchedWatchlistIds.Contains(movie.GetProperty("id").GetInt32())))
            .Where(candidate => !watchedIds.Contains(candidate.Movie.Id) &&
                !personalizedIds.Contains(candidate.Movie.Id) &&
                candidate.Movie.Rating >= 7 && candidate.VoteCount >= 100 &&
                candidate.GenreIds.Count > 0 && candidate.GenreIds.All(rareGenreIds.Contains))
            .OrderBy(candidate => candidate.GenreIds.Max(id => watchedGenreCounts.GetValueOrDefault(id)))
            .ThenByDescending(candidate => candidate.Movie.Rating)
            .ThenByDescending(candidate => candidate.VoteCount)
            .FirstOrDefault();
        if (wildCard is null)
        {
            return null;
        }

        var genre = wildCard.GenreIds.MinBy(id => watchedGenreCounts.GetValueOrDefault(id));
        return (wildCard, namesById[genre]);
    }

    public async Task<string> GetViewingSummaryAsync(int movieId, CancellationToken cancellationToken)
    {
        using var client = CreateTmdbClient();
        var providers = await MovieWatchProviders.GetForIrelandAsync(client, movieId, cancellationToken);
        if (providers.Streaming.Count > 0)
        {
            return $"Stream on {string.Join(" or ", providers.Streaming.Take(2))}";
        }

        return providers.Rental.Count > 0
            ? "Available to rent"
            : "No streaming or rental options listed";
    }

    private async Task<float[]> GetOrCreateEmbeddingAsync(
        string text, CancellationToken cancellationToken)
    {
        var cacheInput = $"{GeminiEmbeddingService.Model}\n{GeminiEmbeddingService.Dimensions}\n{text}";
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheInput)));
        var cached = await _dbContext.MovieEmbeddings
            .Where(embedding => embedding.CacheKey == cacheKey)
            .Select(embedding => embedding.VectorJson)
            .SingleOrDefaultAsync(cancellationToken);
        if (cached is not null)
        {
            return JsonSerializer.Deserialize<float[]>(cached)
                ?? throw new InvalidDataException("The cached movie embedding is empty.");
        }

        var vector = await _gemini.GenerateEmbeddingAsync(text, cancellationToken);
        var vectorJson = JsonSerializer.Serialize(vector);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MovieEmbeddings" ("CacheKey", "VectorJson")
            VALUES ({cacheKey}, {vectorJson})
            ON CONFLICT ("CacheKey") DO NOTHING
            """, cancellationToken);
        return vector;
    }

    private HttpClient CreateTmdbClient()
    {
        var token = _configuration["TMDB:ReadAccessToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("TMDB:ReadAccessToken is required.");
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static double CosineSimilarity(float[] left, float[] right)
    {
        double dotProduct = 0;
        double leftMagnitudeSquared = 0;
        double rightMagnitudeSquared = 0;

        for (var index = 0; index < left.Length; index++)
        {
            double a = left[index];
            double b = right[index];
            dotProduct += a * b;
            leftMagnitudeSquared += a * a;
            rightMagnitudeSquared += b * b;
        }

        if (leftMagnitudeSquared == 0 || rightMagnitudeSquared == 0)
        {
            throw new InvalidDataException("Cannot compare an all-zero embedding.");
        }

        return dotProduct / Math.Sqrt(leftMagnitudeSquared * rightMagnitudeSquared);
    }

    private static async Task<Dictionary<int, string>> GetGenreNamesByIdAsync(
        HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "https://api.themoviedb.org/3/genre/movie/list?language=en", cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream, cancellationToken: cancellationToken);

        var namesById = new Dictionary<int, string>();
        foreach (var genre in document.RootElement.GetProperty("genres").EnumerateArray())
        {
            var name = genre.GetProperty("name").GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                namesById[genre.GetProperty("id").GetInt32()] = name;
            }
        }

        return namesById;
    }

    private static MovieCandidate ReadCandidate(JsonElement movie, bool isFromWatchlist)
    {
        var overview = movie.TryGetProperty("overview", out var overviewElement) &&
            overviewElement.ValueKind == JsonValueKind.String
            ? overviewElement.GetString() ?? ""
            : "";

        var voteCount = movie.TryGetProperty("vote_count", out var voteCountElement) &&
            voteCountElement.ValueKind == JsonValueKind.Number &&
            voteCountElement.TryGetInt32(out var count)
            ? count
            : 0;

        return new MovieCandidate(
            ReadMovie(movie), isFromWatchlist, overview, ReadGenreIds(movie), voteCount);
    }

    private static IReadOnlyList<int> ReadGenreIds(JsonElement movie)
    {
        // Popular results contain IDs. Movie details contain genre objects.
        if (movie.TryGetProperty("genre_ids", out var genreIds) &&
            genreIds.ValueKind == JsonValueKind.Array)
        {
            return genreIds.EnumerateArray().Select(genreId => genreId.GetInt32()).ToArray();
        }

        if (movie.TryGetProperty("genres", out var genres) &&
            genres.ValueKind == JsonValueKind.Array)
        {
            return genres.EnumerateArray().Select(genre => genre.GetProperty("id").GetInt32()).ToArray();
        }

        return Array.Empty<int>();
    }

    private static Movie ReadMovie(JsonElement movie)
    {
        return new Movie
        {
            Id = movie.GetProperty("id").GetInt32(),
            Title = movie.GetProperty("title").GetString() ?? "",
            Rating = movie.GetProperty("vote_average").GetDouble(),
            PosterPath = movie.GetProperty("poster_path").GetString()
        };
    }

    public async Task<(HashSet<int> WatchedIds, HashSet<int> UnwatchedWatchlistIds)>
        GetWatchlistMovieIdsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _dbContext.WatchlistEntries
            .Select(entry => new { entry.MovieId, entry.IsWatched })
            .ToListAsync(cancellationToken);

        var watchedIds = entries
            .Where(entry => entry.IsWatched)
            .Select(entry => entry.MovieId)
            .ToHashSet();

        var unwatchedWatchlistIds = entries
            .Where(entry => !entry.IsWatched && !watchedIds.Contains(entry.MovieId))
            .Select(entry => entry.MovieId)
            .ToHashSet();

        return (watchedIds, unwatchedWatchlistIds);
    }
}
