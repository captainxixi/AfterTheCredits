using System.Net.Http.Headers;
using System.Text.Json;
using AfterTheCredits.Data;
using AfterTheCredits.Models;
using AfterTheCredits.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AfterTheCredits.Pages;

public class IndexModel(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    AppDbContext db,
    DemoVisitorSession session,
    IMemoryCache cache,
    ILogger<IndexModel> logger) : PageModel
{
    private const int StarterBatchSize = 6;

    private static readonly (int Id, string Title)[] StarterMovies =
    [
        (157336, "Interstellar"),
        (313369, "La La Land"),
        (129, "Spirited Away"),
        (419430, "Get Out"),
        (155, "The Dark Knight"),
        (496243, "Parasite"),
        (120467, "The Grand Budapest Hotel"),
        (862, "Toy Story"),
        (597, "Titanic"),
        (105, "Back to the Future"),
        (238, "The Godfather"),
        (329, "Jurassic Park"),
        (27205, "Inception"),
        (4348, "Pride & Prejudice"),
        (98, "Gladiator"),
        (603, "The Matrix"),
        (9377, "Ferris Bueller’s Day Off"),
        (50014, "The Help")
    ];

    [BindProperty(SupportsGet = true)]
    public string? SearchQuery { get; set; }

    public IReadOnlyList<Movie> Movies { get; private set; } = [];
    public HashSet<int> FavoriteMovieIds { get; private set; } = [];
    public int FavoriteTarget => RecommendationService.FavoriteTarget;
    public HashSet<int> WatchlistMovieIds { get; private set; } = [];
    public bool ShowOnboarding { get; private set; }
    public bool CanChangeStarterBatch { get; private set; }
    public int StarterBatchDepth { get; private set; }
    public string? MovieMessage { get; private set; }

    private IQueryable<DemoVisitor> CurrentVisitor => db.DemoVisitors
        .Where(visitor => (Guid?)visitor.Id == session.VisitorId && visitor.ExpiresAtUtc > DateTime.UtcNow);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var visitor = await CurrentVisitor.AsNoTracking().SingleAsync(cancellationToken);
        var entries = await db.WatchlistEntries.AsNoTracking().ToListAsync(cancellationToken);
        WatchlistMovieIds = entries.Select(entry => entry.MovieId).ToHashSet();
        FavoriteMovieIds = entries
            .Where(entry => entry.IsWatched && entry.MyRating >= RecommendationService.FavoriteRating)
            .Select(entry => entry.MovieId)
            .ToHashSet();
        ShowOnboarding = !visitor.OnboardingCompleted;

        var token = configuration["TMDB:ReadAccessToken"];
        if (ShowOnboarding && string.IsNullOrWhiteSpace(SearchQuery))
        {
            var batches = ReadStarterBatches(visitor.SkippedStarterMovieIds);
            StarterBatchDepth = batches.Count;
            var skipped = batches.SelectMany(batch => batch).ToHashSet();
            var availableStarters = StarterMovies
                .Where(movie => !skipped.Contains(movie.Id) && !FavoriteMovieIds.Contains(movie.Id))
                .ToArray();
            CanChangeStarterBatch = availableStarters.Length > StarterBatchSize;
            Movies = await GetStartersAsync(availableStarters.Take(StarterBatchSize).ToArray(),
                token, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            MovieMessage = "Movies are temporarily unavailable.";
            return;
        }

        using var client = CreateTmdbClient(token);
        var url = string.IsNullOrWhiteSpace(SearchQuery)
            ? "https://api.themoviedb.org/3/movie/popular"
            : "https://api.themoviedb.org/3/search/movie?query=" + Uri.EscapeDataString(SearchQuery.Trim());

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            Movies = document.RootElement.GetProperty("results").EnumerateArray().Select(ReadMovie).ToArray();
        }
        catch (Exception exception) when (IsTmdbFailure(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Could not load movies for the home page.");
            MovieMessage = "Movies are temporarily unavailable. Please try again.";
        }
    }

    public async Task<IActionResult> OnPostAsync(int movieId, CancellationToken cancellationToken)
    {
        if (movieId <= 0)
        {
            return BadRequest();
        }

        await db.SaveWatchlistMovieAsync(movieId, cancellationToken);
        return RedirectToPage(new { SearchQuery });
    }

    public async Task<IActionResult> OnPostLikeStarterAsync(int movieId, CancellationToken cancellationToken)
    {
        if (movieId <= 0 || !ModelState.IsValid)
        {
            return BadRequest();
        }

        await db.SaveWatchlistMovieAsync(movieId, cancellationToken, isWatched: true,
            myRating: RecommendationService.FavoriteRating, keepHigherRating: true);
        return RedirectToPage(new { SearchQuery });
    }

    public async Task<IActionResult> OnPostChangeStarterBatchAsync(
        int[] movieIds, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || movieIds.Length != StarterBatchSize ||
            movieIds.Distinct().Count() != movieIds.Length ||
            movieIds.Any(id => !StarterMovies.Any(movie => movie.Id == id)))
        {
            return BadRequest();
        }

        var previous = await ReadCurrentStarterHistoryAsync(cancellationToken);
        if (previous is not null)
        {
            var batches = ReadStarterBatches(previous);
            if (!batches.SelectMany(batch => batch).Intersect(movieIds).Any())
            {
                batches.Add(movieIds);
                await SaveStarterBatchesAsync(previous, batches, cancellationToken);
            }
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPreviousStarterBatchAsync(
        int batchDepth, CancellationToken cancellationToken)
    {
        var previous = await ReadCurrentStarterHistoryAsync(cancellationToken);
        if (previous is not null)
        {
            var batches = ReadStarterBatches(previous);
            if (batchDepth > 0 && batches.Count == batchDepth)
            {
                batches.RemoveAt(batches.Count - 1);
                await SaveStarterBatchesAsync(previous, batches, cancellationToken);
            }
        }

        return RedirectToPage();
    }

    private Task<string?> ReadCurrentStarterHistoryAsync(CancellationToken cancellationToken) =>
        CurrentVisitor.Where(visitor => !visitor.OnboardingCompleted)
            .Select(visitor => visitor.SkippedStarterMovieIds)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<int> SaveStarterBatchesAsync(
        string previous, List<int[]> batches, CancellationToken cancellationToken)
    {
        var history = JsonSerializer.Serialize(batches);
        return CurrentVisitor.Where(visitor =>
                !visitor.OnboardingCompleted && visitor.SkippedStarterMovieIds == previous)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(visitor => visitor.SkippedStarterMovieIds, history),
                cancellationToken);
    }

    private static List<int[]> ReadStarterBatches(string history)
    {
        using var document = JsonDocument.Parse(history);
        var items = document.RootElement;
        if (items.GetArrayLength() == 0)
        {
            return [];
        }

        if (items[0].ValueKind == JsonValueKind.Array)
        {
            return items.Deserialize<List<int[]>>() ?? [];
        }

        // Earlier sessions stored a flat set of skipped IDs. Restore their display order.
        var skipped = items.EnumerateArray().Select(item => item.GetInt32()).ToHashSet();
        return StarterMovies.Where(movie => skipped.Contains(movie.Id))
            .Select(movie => movie.Id)
            .Chunk(StarterBatchSize)
            .ToList();
    }

    public async Task<IActionResult> OnPostFinishOnboardingAsync(CancellationToken cancellationToken)
    {
        await CurrentVisitor.ExecuteUpdateAsync(
            setters => setters.SetProperty(visitor => visitor.OnboardingCompleted, true), cancellationToken);
        return RedirectToPage("/Recommendations");
    }

    private async Task<Movie[]> GetStartersAsync(
        (int Id, string Title)[] starters, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || starters.Length == 0)
        {
            return starters.Select(starter => new Movie { Id = starter.Id, Title = starter.Title }).ToArray();
        }

        using var client = CreateTmdbClient(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));

        return await Task.WhenAll(starters.Select(async starter =>
        {
            var cacheKey = $"onboarding-tmdb:{starter.Id}";
            if (cache.TryGetValue<Movie>(cacheKey, out var cached) && cached is not null)
            {
                return cached;
            }

            try
            {
                using var response = await client.GetAsync(
                    $"https://api.themoviedb.org/3/movie/{starter.Id}", timeout.Token);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
                var movie = ReadMovie(document.RootElement);
                cache.Set(cacheKey, movie, TimeSpan.FromHours(1));
                return movie;
            }
            catch (Exception exception) when (IsTmdbFailure(exception, cancellationToken))
            {
                logger.LogDebug(exception, "Could not load starter movie {MovieId}.", starter.Id);
                return new Movie { Id = starter.Id, Title = starter.Title };
            }
        }));
    }

    private HttpClient CreateTmdbClient(string token)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Movie ReadMovie(JsonElement movie) => new()
    {
        Id = movie.GetProperty("id").GetInt32(),
        Title = movie.GetProperty("title").GetString() ?? "",
        Rating = movie.GetProperty("vote_average").GetDouble(),
        PosterPath = movie.GetProperty("poster_path").GetString()
    };

    private static bool IsTmdbFailure(Exception exception, CancellationToken requestToken) =>
        exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException ||
        exception is OperationCanceledException && !requestToken.IsCancellationRequested;
}
