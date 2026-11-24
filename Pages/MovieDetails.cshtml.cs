using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;
using System.Text.Json;
using AfterTheCredits.Models;
using AfterTheCredits.Data;
using AfterTheCredits.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace AfterTheCredits.Pages;

public class MovieDetailsModel : PageModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Overview { get; set; } = "";
    public string? PosterPath { get; set; }
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _dbContext;
    public string ReleaseDate { get; set; } = "";
    public double Rating { get; set; }
    public int Runtime { get; set; }
    public bool IsInWatchlist { get; private set; }
    public bool IsWatched { get; private set; }
    public double? MyRating { get; private set; }

    public List<Genre> Genres { get; set; } = new();
    public List<string> StreamingProviders { get; set; } = new();
    public List<string> RentalProviders { get; set; } = new();
    public List<string> PurchaseProviders { get; set; } = new();
    public MovieDetailsModel(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        AppDbContext dbContext)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _dbContext = dbContext;
    }

    public async Task OnGetAsync(int id)
    {
        Id = id;
        var watchlistEntry = await _dbContext.WatchlistEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.MovieId == id);

        IsInWatchlist = watchlistEntry is not null;
        IsWatched = watchlistEntry?.IsWatched ?? false;
        MyRating = watchlistEntry?.MyRating;

        var token = _configuration["TMDB:ReadAccessToken"];

        using var client = _httpClientFactory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var url = $"https://api.themoviedb.org/3/movie/{id}";

        using var response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);

        Title = document.RootElement.GetProperty("title").GetString() ?? "";

        Overview = document.RootElement.GetProperty("overview").GetString() ?? "";

        PosterPath = document.RootElement.GetProperty("poster_path").GetString();

        ReleaseDate = document.RootElement.GetProperty("release_date").GetString() ?? "";

        Rating = document.RootElement.GetProperty("vote_average").GetDouble();

        Runtime = document.RootElement.GetProperty("runtime").GetInt32();

        foreach (var genreElement in document.RootElement
             .GetProperty("genres").EnumerateArray())
        {
            var genre = new Genre
            {
                Id = genreElement.GetProperty("id").GetInt32(),
                Name = genreElement.GetProperty("name").GetString() ?? ""
            };

            Genres.Add(genre);
        }
        var providers = await MovieWatchProviders.GetForIrelandAsync(client, id, HttpContext.RequestAborted);
        StreamingProviders = providers.Streaming;
        RentalProviders = providers.Rental;
        PurchaseProviders = providers.Purchase;
    }

    public async Task<IActionResult> OnPostAsync([FromRoute] int id)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        await _dbContext.SaveWatchlistMovieAsync(id, HttpContext.RequestAborted);

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetWatchedAsync([FromRoute] int id, [FromForm] bool? isWatched)
    {
        if (id <= 0 || !ModelState.IsValid || isWatched is null)
        {
            return BadRequest();
        }

        await _dbContext.SaveWatchlistMovieAsync(id, HttpContext.RequestAborted, isWatched: isWatched);

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSaveRatingAsync([FromRoute] int id, [FromForm] double? myRating)
    {
        if (id <= 0 || !ModelState.IsValid || myRating is not double rating ||
            !double.IsFinite(rating) || rating < 0 || rating > 10)
        {
            return BadRequest("Rating must be between 0 and 10.");
        }

        await _dbContext.SaveWatchlistMovieAsync(id, HttpContext.RequestAborted,
            isWatched: true, myRating: rating);

        return RedirectToPage(new { id });
    }
}
