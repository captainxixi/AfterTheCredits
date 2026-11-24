using Microsoft.AspNetCore.Mvc.RazorPages;
using AfterTheCredits.Data;
using AfterTheCredits.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AfterTheCredits.Pages;

public class WatchlistModel : PageModel
{
    private readonly AppDbContext _dbContext;
    public List<WatchlistEntry> Entries { get; set; } = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    public List<Movie> Movies { get; set; } = new();
    public Dictionary<int, double> GenreScores { get; set; } = new();
    public List<string> TopGenres { get; set; } = new();
    [BindProperty(SupportsGet = true)]
    public bool Watched { get; set; }
    public int WatchedCount => Entries.Count(entry => entry.IsWatched);
    public int WatchlistCount => Entries.Count - WatchedCount;
    public IEnumerable<Movie> VisibleMovies => Movies.Where(movie => movie.IsWatched == Watched);

    public WatchlistModel(AppDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }
    public async Task<IActionResult> OnPostToggleWatchedAsync(int movieId)
    {
        var updated = await _dbContext.WatchlistEntries.Where(entry => entry.MovieId == movieId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.IsWatched, entry => !entry.IsWatched),
                HttpContext.RequestAborted);
        if (updated == 0)
        {
            return NotFound();
        }

        return RedirectToPage(new { watched = !Watched });
    }
    public async Task<IActionResult> OnPostSaveRatingAsync(
    int movieId, double? myRating)
    {
        if (!ModelState.IsValid || myRating is not double rating ||
            !double.IsFinite(rating) || rating < 0 || rating > 10)
        {
            return BadRequest("Rating must be between 0 and 10.");
        }

        var updated = await _dbContext.WatchlistEntries.Where(entry => entry.MovieId == movieId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entry => entry.MyRating, myRating)
                .SetProperty(entry => entry.IsWatched, true),
                HttpContext.RequestAborted);
        if (updated == 0)
        {
            return NotFound();
        }

        return RedirectToPage(new { watched = true });
    }
    public async Task<IActionResult> OnPostRemoveAsync(int movieId)
    {
        var removed = await _dbContext.WatchlistEntries.Where(entry => entry.MovieId == movieId)
            .ExecuteDeleteAsync(HttpContext.RequestAborted);
        if (removed == 0)
        {
            return NotFound();
        }

        return RedirectToPage(new { watched = Watched });
    }
    public async Task OnGetAsync()
    {
        Entries = await _dbContext.WatchlistEntries.AsNoTracking().ToListAsync();
        var token = _configuration["TMDB:ReadAccessToken"];

        using var client = _httpClientFactory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        foreach (var entry in Entries)
        {
            var response = await client.GetAsync(
                $"https://api.themoviedb.org/3/movie/{entry.MovieId}");

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(json);

            string title = document.RootElement
                .GetProperty("title")
                .GetString() ?? "";

            var genres = new List<Genre>();

            foreach (var genreElement in document.RootElement
                         .GetProperty("genres").EnumerateArray())
            {
                var genre = new Genre
                {
                    Id = genreElement.GetProperty("id").GetInt32(),
                    Name = genreElement.GetProperty("name").GetString() ?? ""
                };

                genres.Add(genre);
            }

            Movies.Add(new Movie
            {
                Id = entry.MovieId,
                Title = title,
                Rating = document.RootElement.GetProperty("vote_average").GetDouble(),
                PosterPath = document.RootElement.GetProperty("poster_path").GetString(),
                IsWatched = entry.IsWatched,
                MyRating = entry.MyRating,
                Genres = genres,
            });
        }
        foreach (var movie in Movies)
        {
            if (!movie.IsWatched || !movie.MyRating.HasValue)
            {
                continue;
            }

            foreach (var genre in movie.Genres)
            {
                if (!GenreScores.ContainsKey(genre.Id))
                {
                    GenreScores[genre.Id] = 0;
                }

                GenreScores[genre.Id] += movie.MyRating.Value;
            }
        }
        foreach (var score in GenreScores)
        {
            Console.WriteLine($"Genre ID: {score.Key}, Score: {score.Value}");
        }

        var sortedGenres = GenreScores
            .OrderByDescending(g => g.Value)
            .Take(3)
            .ToList();

        foreach (var genre in sortedGenres)
        {
            var genreName = Movies
                .SelectMany(m => m.Genres)
                .First(g => g.Id == genre.Key)
                .Name;

            TopGenres.Add(genreName);
        }

        foreach (var genre in sortedGenres)
        {
            Console.WriteLine($"Top Genre: {genre.Key}, Score: {genre.Value}");
        }
    }
}
