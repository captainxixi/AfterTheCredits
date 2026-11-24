namespace AfterTheCredits.Models;

public sealed class DemoVisitor
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool OnboardingCompleted { get; set; }
    public string SkippedStarterMovieIds { get; set; } = "[]";
}
