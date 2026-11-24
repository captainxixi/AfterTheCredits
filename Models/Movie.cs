namespace AfterTheCredits.Models;

public class Movie
{
    public int Id {get; set; }
    public string Title {get; set; } = "";
    public double Rating {get; set; }
    public string? PosterPath {get; set; }
    public bool IsWatched { get; set; }
    public double? MyRating { get; set; }
    public List<Genre> Genres { get; set; } = new();
}