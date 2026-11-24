using System.Text.Json;
using AfterTheCredits.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AfterTheCredits.Pages;

public class RecommendationsModel(
    RecommendationService recommendationService,
    ILogger<RecommendationsModel> logger) : PageModel
{
    public IReadOnlyList<MovieCandidate> Candidates { get; private set; } =
        Array.Empty<MovieCandidate>();

    public IReadOnlyDictionary<int, string?> RecommendationReasons { get; private set; } =
        new Dictionary<int, string?>();

    public IReadOnlyDictionary<int, string> ViewingSummaries { get; private set; } =
        new Dictionary<int, string>();

    public int? WildCardMovieId { get; private set; }
    public string? WildCardGenre { get; private set; }
    public string? WildCardMessage { get; private set; }

    public string? RankingMessage { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Candidates = await recommendationService.GenerateCandidatesAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not load recommendation candidates from TMDB.");
            ErrorMessage = "Movie candidates are temporarily unavailable. Please try again.";
            return;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out loading recommendation candidates from TMDB.");
            ErrorMessage = "Loading movie candidates took too long. Please try again.";
            return;
        }

        if (Candidates.Count == 0)
        {
            return;
        }

        using var rankingTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        rankingTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var ranked = await recommendationService.RankCandidatesAsync(Candidates, rankingTimeout.Token);
            var personalized = ranked.Take(4).ToList();
            if (personalized.Count == 0)
            {
                RankingMessage = "Popular picks to get you started. Rating movies you’ve seen helps personalize future picks.";
            }
            else
            {
                RecommendationReasons = personalized.ToDictionary(
                    result => result.Candidate.Movie.Id, DescribeMatch);
                Candidates = personalized.Select(result => result.Candidate).ToList();
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or InvalidDataException or
                InvalidOperationException or KeyNotFoundException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not rank recommendation candidates.");
            RankingMessage = "Personalized ranking is temporarily unavailable. Here are some popular picks.";
        }

        if (RecommendationReasons.Count > 0)
        {
            await LoadWildCardAsync(cancellationToken);
        }
        else
        {
            Candidates = Candidates.Take(4).ToList();
        }

        using var providerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        providerTimeout.CancelAfter(TimeSpan.FromSeconds(4));
        var summaries = await Task.WhenAll(Candidates.Select(async candidate =>
        {
            var id = candidate.Movie.Id;
            try
            {
                return (Id: id, Summary: (string?)await recommendationService.GetViewingSummaryAsync(
                    id, providerTimeout.Token));
            }
            catch (Exception exception) when (
                exception is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException ||
                exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(exception, "Could not load viewing options for movie {MovieId}.", id);
                return (Id: id, Summary: (string?)null);
            }
        }));
        ViewingSummaries = summaries.Where(result => result.Summary is not null)
            .ToDictionary(result => result.Id, result => result.Summary!);
    }

    private async Task LoadWildCardAsync(CancellationToken cancellationToken)
    {
        using var wildCardTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wildCardTimeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var wildCard = await recommendationService.FindWildCardAsync(
                RecommendationReasons.Keys.ToHashSet(), wildCardTimeout.Token);
            if (wildCard is not { } pick)
            {
                WildCardMessage = "No Wild Card met the genre, rating, and vote requirements.";
            }
            else
            {
                Candidates = Candidates.Append(pick.Movie).ToList();
                WildCardMovieId = pick.Movie.Movie.Id;
                WildCardGenre = pick.Genre;
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not load a Wild Card.");
            WildCardMessage = "Wild Card is temporarily unavailable.";
        }
    }

    private static string? DescribeMatch(RankedMovieCandidate result)
    {
        var sharedGenres = result.Candidate.GenreNames
            .Intersect(result.ClosestReference.GenreNames, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        var referenceTitle = result.ClosestReference.Movie.Title;

        return sharedGenres.Length == 0
            ? null
            : $"Shares {string.Join(" & ", sharedGenres)} with {referenceTitle}";
    }
}
