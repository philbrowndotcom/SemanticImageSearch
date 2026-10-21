# Semantic Image Search

Search your own photo library by describing what's in the picture, for example *"dog playing on the beach"* or *"birthday cake with candles"*, with an optional date-taken range.

Everything runs locally. Images are captioned by a vision model (LLaVA), the captions are turned into vector embeddings, and the vectors are stored in Qdrant. A search embeds your query the same way and returns the photos with the closest captions.

## How it works

```
				 Indexing (one-off / repeatable)
 ┌────────────┐   image    ┌──────────────┐  caption   ┌──────────────────┐
 │  Indexer   │ ─────────▶ │ Ollama: llava│ ─────────▶ │ Ollama:          │
 │ (console)  │            └──────────────┘            │ nomic-embed-text │
 └────────────┘ ◀────────────── 768-dim vector ─────── └──────────────────┘
	   │
	   │ vector + payload (file_path, description, date_taken, ...)
	   ▼
 ┌────────────┐
 │   Qdrant   │  collection: image_library
 └────────────┘
	   ▲
	   │ vector query + optional date_taken filter
 ┌────────────┐  /api/search   ┌────────────┐
 │    API     │ ◀───────────── │  React UI  │
 │ (ASP.NET)  │ ─────────────▶ │   (Vite)   │
 └────────────┘  results +     └────────────┘
				 /api/images/{id}
```

1. **Indexing:** the Indexer scans a folder for images. For each one it:
   - asks LLaVA (via Ollama) for a short, factual description;
   - embeds that description with `nomic-embed-text` (768 dimensions);
   - reads the capture date from EXIF;
   - stores the vector and metadata in Qdrant.
2. **Searching:** the API embeds the search text with the same model and asks Qdrant for the nearest vectors (cosine similarity). It can also filter on `date_taken`.
3. **Viewing:** the UI shows the results as a thumbnail grid. Images are served by the API by their Qdrant point ID, never by file path.

## Projects

| Project | Type | Purpose |
|---|---|---|
| `SemanticImageSearch.Core` | Class library (.NET 10) | Shared models and services: `OllamaAIService` (captioning and embeddings), `QdrantVectorService` (collection setup and upserts), `ExifDateReader` (capture date). |
| `SemanticImageSearch.Indexer` | Console app (.NET 10) | Scans the picture folder and indexes every image into Qdrant. |
| `SemanticImageSearch.API` | ASP.NET Core Web API (.NET 10) | `GET /api/search` and `GET /api/images/{id}`. |
| `SemanticImageSearch.Web` | React 19 + TypeScript + Vite | Search UI with an optional date range and a thumbnail grid. |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (current LTS)
- [Ollama](https://ollama.com/) with these two models:
  ```powershell
  ollama pull llava
  ollama pull nomic-embed-text
  ```
- [Qdrant](https://qdrant.tech/). The easiest way to run it is with Docker:
  ```powershell
  docker run -p 6333:6333 -p 6334:6334 -v qdrant_storage:/qdrant/storage qdrant/qdrant
  ```
  Port `6334` (gRPC) is used by the .NET client. Port `6333` serves the REST API and the dashboard at <http://localhost:6333/dashboard>.

## Getting started

### 1. Point the Indexer at your photos

Set `Indexer:PictureLibraryPath` in `SemanticImageSearch.Indexer/appsettings.json`:

```json
"Indexer": {
  "PictureLibraryPath": "C:\\Path\\To\\Your\\Photos"
}
```

Or pass it on the command line (see below). Subfolders are included. Supported formats: `.jpg`, `.jpeg`, `.png`, `.webp` (configurable via `Indexer:SupportedExtensions`).

### 2. Index the images

```powershell
dotnet run --project SemanticImageSearch.Indexer

# or override the folder for this run
dotnet run --project SemanticImageSearch.Indexer -- --path "C:\Path\To\Your\Photos"
```

The Indexer creates the `image_library` collection if needed and prints each caption as it goes. On later runs it only processes **new** images; files already in the collection are skipped.

### 3. Start the API

```powershell
dotnet run --project SemanticImageSearch.API --launch-profile http
```

The API listens on <http://localhost:5256>.

### 4. Start the UI

```powershell
cd SemanticImageSearch.Web
npm install
npm run dev
```

Open <http://localhost:54879>. The Vite dev server forwards `/api` requests to the API.

> **PowerShell tip:** if `npm` fails with *"running scripts is disabled on this system"*, use `npm.cmd` instead (e.g. `npm.cmd run dev`).

### Running from Visual Studio

Right-click the solution, choose **Configure Startup Projects**, then **Multiple startup projects**. Set `SemanticImageSearch.API` and `semanticimagesearch.web` to **Start**, then press F5. Run the Indexer separately when you want to (re)index.

## API reference

### `GET /api/search`

| Parameter | Required | Description |
|---|---|---|
| `query` | yes | Text describing what you're looking for. |
| `limit` | no | Number of results, 1–100 (default 10). |
| `from` | no | Earliest date taken, `yyyy-MM-dd`, inclusive. |
| `to` | no | Latest date taken, `yyyy-MM-dd`, inclusive of the whole day. |

Example:

```
GET /api/search?query=dog%20on%20the%20beach&from=2023-01-01&to=2023-12-31&limit=10
```

Response:

```json
[
  {
	"id": "011caa7b-b80a-4408-8ab1-2c73d8fc22da",
	"filePath": "D:\\Media\\images\\...\\IMG_1234.jpg",
	"description": "A brown dog runs along a sandy beach next to the water...",
	"dateTaken": "2023-07-04T18:22:10.0000000Z",
	"score": 0.78
  }
]
```

`score` is the cosine similarity (higher is closer). The request returns `400` if `query` is missing or `from` is after `to`.

### `GET /api/images/{id}`

Returns the image file for a Qdrant point ID, with the correct content type. Returns `404` if the point or the file doesn't exist. Lookup is by ID only, so the browser can't request arbitrary files from disk.

Sample requests for both endpoints are in `SemanticImageSearch.API/SemanticImageSearch.API.http`.

## Data stored in Qdrant

Collection `image_library`, 768-dimension vectors, cosine distance. Each point's payload contains:

| Field | Description |
|---|---|
| `file_path` | Full path to the image on disk. |
| `description` | The LLaVA caption that was embedded. |
| `date_taken` | Capture date in UTC (ISO 8601). Comes from EXIF `DateTimeOriginal`, then `DateTimeDigitized`, then `DateTime`, and falls back to the file's creation time. EXIF times have no time zone, so they're treated as local time. |
| `last_modified_utc` | File's last-write time in UTC. |

A datetime payload index is created on `date_taken` to keep date-range filtering fast.

## Configuration

Settings live in `appsettings.json` for the Indexer and the API. Both share the `Qdrant` and `Ollama` sections. Keep `CollectionName` and `EmbeddingModel` identical in both, or searches won't match what was indexed.

| Key | Used by | Default |
|---|---|---|
| `Indexer:PictureLibraryPath` | Indexer | `D:\Media\images\Phone pics\Camera` |
| `Indexer:SupportedExtensions` | Indexer | `.jpg`, `.jpeg`, `.png`, `.webp` |
| `Qdrant:Host` / `Qdrant:Port` | Indexer, API | `localhost` / `6334` (gRPC) |
| `Qdrant:CollectionName` | Indexer, API | `image_library` |
| `Qdrant:VectorSize` | Indexer | `768` (must match the embedding model) |
| `Ollama:BaseUrl` | Indexer, API | `http://localhost:11434` |
| `Ollama:VisionModel` | Indexer | `llava` |
| `Ollama:EmbeddingModel` | Indexer, API | `nomic-embed-text` |
| `Ollama:Temperature` | Indexer | `0.2` |
| `Ollama:DescriptionPrompt` | Indexer | Factual, 2–4 sentences (see `OllamaOptions.cs`) |
| `Cors:AllowedOrigins` | API | `http://localhost:54879`, `https://localhost:54879` |

### Overriding settings

Later sources override earlier ones:

1. `appsettings.json`
2. Environment variables. The Indexer uses a `SIS_` prefix, e.g. `SIS_Indexer__PictureLibraryPath`; the API uses standard ASP.NET Core names, e.g. `Qdrant__Host`.
3. Command-line arguments, e.g. `--Qdrant:Host myserver`. The Indexer also accepts `--path` / `-p` as a shortcut for `--Indexer:PictureLibraryPath`.

Examples:

```powershell
dotnet run --project SemanticImageSearch.Indexer -- --path "E:\Photos" --Ollama:VisionModel llava:13b
dotnet run --project SemanticImageSearch.API --launch-profile http -- --Qdrant:Host qdrant.local
```

### Ports

These stay in their standard tooling files:

| Setting | Location | Default |
|---|---|---|
| API port | `SemanticImageSearch.API/Properties/launchSettings.json` | `5256` |
| UI port and API proxy | `SemanticImageSearch.Web/vite.config.ts` | `54879` → `http://localhost:5256` |

## Incremental indexing and re-indexing

At startup the Indexer reads every `file_path` already stored in Qdrant and skips those files, so only newly added images are captioned. Each point ID is derived from the file's full path, so the same file always maps to the same point and can never be stored twice.

To re-index everything, for example after changing the caption prompt:

1. Delete the `image_library` collection in the Qdrant dashboard (<http://localhost:6333/dashboard>), or with:
   ```powershell
   Invoke-RestMethod -Method Delete http://localhost:6333/collections/image_library
   ```
2. Run the Indexer again.

## Limitations and ideas

- Images are assumed not to change once indexed. Edited files aren't re-captioned, and deleted or moved files aren't removed from the collection (a moved file is indexed again under its new path).
- The API reads images straight from their original paths, so it must run on the same machine as the photo library (or with the same paths mounted).
- Captions are only as good as LLaVA's interpretation of each photo. Text search matches the caption, not the pixels.
