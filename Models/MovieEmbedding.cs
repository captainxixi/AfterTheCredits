namespace AfterTheCredits.Models;

public sealed class MovieEmbedding
{
    public string CacheKey { get; set; } = "";
    public string VectorJson { get; set; } = "";
}
