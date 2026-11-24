# After the Credits

**Spend less time choosing a movie and more time watching one.**

[Try the live demo](https://afterthecredits-production.up.railway.app/) · Hosted on Railway · No sign-up required

After the Credits is a full-stack movie discovery web application. Instead of presenting an endless feed, it aims to offer four personalized movie picks and one Wild Card that explores a genre the viewer rarely watches. Each pick includes viewing availability in Ireland when that information is available.

![Tonight’s picks: four personalized movie recommendations and one Wild Card, with viewing availability in Ireland.](docs/screenshots/recommendations.png)

## Features

- **Cold-start onboarding:** Aim to select five movies you have seen and liked from a curated starter collection, or find favorites through search. You can also start with fewer movies or skip onboarding and browse popular picks.
- **Personalized recommendations:** Gemini text embeddings capture movie titles, genres and plot summaries. Cosine similarity ranks unseen candidates against the viewer's highly rated movies.
- **Wild Card:** Discover a well-rated movie from an underrepresented genre in the viewer's watch history.
- **My Movies:** Manage separate Watchlist and Watched views. Rating a movie automatically marks it as watched and excludes it from future recommendations.
- **Viewing availability:** See subscription and rental options in Ireland, when provided by TMDB.
- **Anonymous demo sessions:** No account is required. Each visitor's ratings and watch history are isolated, expire after 24 hours, and can be cleared with Reset Demo.

## User experience

### Cold-start onboarding

Choose films you have seen and enjoyed to help establish your preferences.

![Cold-start onboarding with six starter movies, search, and a favorites counter.](docs/screenshots/onboarding.png)

<details>
<summary>My Movies — Watchlist and Watched</summary>

Save films for later in Watchlist, and record watched films and personal ratings.

![Watchlist with three saved, unwatched movies and rating controls.](docs/screenshots/watchlist.png)

![Watched movies with personal ratings and favorite genres.](docs/screenshots/watched.png)

</details>

## How recommendations work

1. Build a candidate pool from popular TMDB movies and unwatched movies from the visitor's watchlist. Exclude watched movies and deduplicate by TMDB ID.
2. Generate 768-dimensional Gemini embeddings for each movie's structured text. Store them in SQLite so identical inputs can reuse cached vectors.
3. Compare each candidate with up to five watched movies rated 8/10 or higher and rank by mean cosine similarity. Display up to four personalized picks, with shared genres shown where applicable.
4. Build a separate Wild Card pool from genres appearing in at most 20% of the viewer's known watch history. Require a TMDB rating of at least 7/10 and at least 100 votes, and exclude watched movies and the personalized picks.
5. Retrieve streaming and rental availability for the selected movies. If personalization is unavailable, show a short list of popular picks.

## Tech stack

| Layer | Technology |
| --- | --- |
| Web application | C#, .NET 10, ASP.NET Core Razor Pages |
| UI | HTML, CSS, Bootstrap, JavaScript |
| Data | SQLite, Entity Framework Core |
| Movie metadata | TMDB API |
| Semantic recommendations | Gemini Embedding 2 |
| Session isolation | ASP.NET Core Data Protection, HTTP-only cookies |

The cookie contains a protected visitor identifier. Watchlists, ratings and session expiry are stored in SQLite, while embeddings generated from public movie metadata are shared across sessions.

## Run locally

Prerequisites: .NET 10 SDK, a TMDB read access token, and a Gemini API key.

```bash
git clone https://github.com/captainxixi/AfterTheCredits.git
cd AfterTheCredits
dotnet user-secrets set "TMDB:ReadAccessToken" "YOUR_TMDB_READ_ACCESS_TOKEN"
dotnet user-secrets set "Gemini:ApiKey" "YOUR_GEMINI_API_KEY"
dotnet run
```

Open [http://localhost:5118](http://localhost:5118). EF Core applies database migrations automatically at startup. API credentials and the local SQLite database are excluded from version control.

## Deployment

For a single-instance deployment, configure the following environment variables:

| Variable | Purpose |
| --- | --- |
| `TMDB__ReadAccessToken` | TMDB API authentication |
| `Gemini__ApiKey` | Gemini API authentication |
| `ConnectionStrings__DefaultConnection` | SQLite connection string pointing to a persistent disk, with foreign keys enabled |
| `DataProtection__KeysPath` | Persistent directory for cookie protection keys |

Persist both the SQLite database and data protection keys across restarts. The application can initialize a fresh database on first startup.

## Data sources

Movie data and posters are provided by [The Movie Database (TMDB)](https://www.themoviedb.org/). Streaming availability is provided by [JustWatch](https://www.justwatch.com/ie) through TMDB.

This application uses the TMDB API but is not endorsed or certified by TMDB.
