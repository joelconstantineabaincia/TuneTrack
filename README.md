# TuneTrack

TuneTrack is a Blazor Web App with Tailwind CSS that analyzes playlist links from Spotify, YouTube Music, Apple Music, and more to inspect genres, score playlist diversity, and recommend tailored tracks to expand sonic horizons.

## Database Setup (Supabase PostgreSQL)

TuneTrack uses Entity Framework Core with Npgsql to connect to a PostgreSQL database hosted on Supabase.

### 1. Supabase Connection String
In your Supabase dashboard:
1. Navigate to **Project Settings** > **Database**.
2. Under **Connection Pooling**, choose **Session mode** (Port `5432` or pooler host).
3. Ensure the connection string includes `SSL Mode=Require;`.

Example connection string format:
```text
Host=aws-0-[region].pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.[project-ref];Password=[YOUR_PASSWORD];SSL Mode=Require;
```

### 2. Configure via User Secrets
To keep credentials secure without hardcoding them into source control, use `dotnet user-secrets`:

```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=aws-0-[region].pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.[project-ref];Password=your_actual_password;SSL Mode=Require;"
```

### 3. Apply Migrations
To update your Supabase database schema to the latest migration:

```bash
dotnet dotnet-ef database update
```

### In-Memory Fallback Mode
If `ConnectionStrings:DefaultConnection` is absent, empty, or contains placeholder values (`YOUR_PASSWORD`, `[PASSWORD]`, `changeme`), TuneTrack automatically falls back to EF Core In-Memory mode and logs a warning on startup. The application remains fully functional for testing and local development without requiring an active PostgreSQL database.
