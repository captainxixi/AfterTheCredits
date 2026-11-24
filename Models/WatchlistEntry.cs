namespace AfterTheCredits.Models;

public class WatchlistEntry
{
    public int Id {get; set; }
    public Guid VisitorId { get; set; }
    public int MovieId {get; set; }
    public bool IsWatched {get; set; }
    public double? MyRating {get; set; }
    
}