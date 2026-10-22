# Semantic Image Search

Search your own photo library by describing what's in the picture, for example *"dog playing on the beach"* or *"birthday cake with candles"*, with an optional date-taken range.

Everything runs locally. Images are captioned by a vision model (LLaVA), the captions are turned into vector embeddings, and the vectors are stored in Qdrant. A search embeds your query the same way and returns the photos with the closest captions.

## How it works

```
				 Indexing (one-off / repeatable)
  ┌────────────┐   image    ┌──────────────┐  caption   ┌──────────────────┐
 │  Indexer   │ ─────────▶ │ Ollama: llava│ ─────────▶ │ Ollama:          │
 │ (console)  │            └──────────────┘            │ nomic-embed-text │
 └──────┬─────┘ ◀────────────── 768-dim vector ─────── └──────────────────┘
		│
		│ face boxes → ArcFace embeddings → reference matches
		▼
 ┌────────────┐      ┌──────────────┐      ┌────────────────┐
 │ SCRFD      │ ───▶ │ ArcFace      │ ───▶ │ FaceRegistry   │
 │ detector   │      │ recognizer   │      │ ReferenceFaces │
 └────────────┘      └──────────────┘      └────────────────┘
	   │
		  │ vector + payload (file_path, description, people_names, date_taken, ...)
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
3. **People recognition:** the Indexer detects faces with SCRFD, generates an ArcFace embedding for each detected face, and compares it with the embeddings loaded from `ReferenceFaces`. Matches at or above the current `0.45` similarity threshold are stored as the independent `people_names` payload field. Names are not appended to the LLaVA description, so semantic captions remain factual image descriptions.
4. **Viewing:** the UI shows the results as a thumbnail grid. Images are served by the API by their Qdrant point ID, never by file path.

## Face detection and recognition pipeline

Face recognition runs during indexing, alongside captioning and embedding generation. It is a separate pipeline from LLaVA semantic description:

1. **SCRFD face detection:** `ScrfdFaceDetector` uses `MLModels/2.5g_bnkps.onnx` to locate faces in the image and return bounding boxes with detection confidence.
2. **Face embedding:** `ArcFaceRecognizer` uses `MLModels/w600k_mbf.onnx` to generate a numerical embedding for each detected face. The embedding represents facial features for similarity comparison; it is not a name by itself.
3. **Reference registry:** `FaceRegistry` loads reference images from `ReferenceFaces`. A person's name comes from the reference image's immediate subfolder, or from the filename when the image is directly in `ReferenceFaces`:

   ```text
   ReferenceFaces/
   ├── Alice/
   │   ├── alice-1.jpg
   │   └── alice-2.jpg
   ├── Bob/
   │   └── bob-1.jpg
   └── Charlie.jpg
   ```

   The first two images register the person as `Alice`, the third as `Bob`, and `Charlie.jpg` registers the person as `Charlie`. Multiple reference images improve matching across poses and lighting.
4. **Matching:** each photo face is compared with the registered embeddings. The Indexer currently considers a match when similarity is at least `0.45`. Recognized names are deduplicated and stored as an array in the image's `people_names` Qdrant payload.
5. **Searching:** `GET /api/people` returns the distinct recognized names found in indexed payloads. The UI uses that list for its multi-select. Selected names are sent as repeated `people` parameters to `GET /api/search`; an image must contain all selected people. People/date-only searches do not call Ollama because they use Qdrant payload filters directly.

The current face matching identifies people only when a suitable reference face exists. Faces that do not meet the threshold remain unnamed and are not added to `people_names`.

## Projects

| Project | Type | Purpose |
|---|---|---|
| `SemanticImageSearch.Core` | Class library (.NET 10) | Shared models and services: `OllamaAIService` (captioning and embeddings), `QdrantVectorService` (collection setup and upserts), `ExifDateReader` (capture date). |
| `SemanticImageSearch.Indexer` | Console app (.NET 10) | Scans the picture folder, captions images, detects and matches faces, and indexes each image into Qdrant. |
| `SemanticImageSearch.API` | ASP.NET Core Web API (.NET 10) | `GET /api/search`, `GET /api/people`, and `GET /api/images/{id}`. |
| `SemanticImageSearch.Web` | React 19 + TypeScript + Vite | Search UI with text, date and recognized-people filters plus a thumbnail grid. |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (current LTS)
- [Ollama](https://ollama.com/) with these two models:
  ```powershell
  ollama pull llava
  ollama pull nomic-embed-text
  ```
- The face-recognition ONNX models expected by the Indexer:
  - `MLModels/2.5g_bnkps.onnx` — SCRFD face detector.
  - `MLModels/w600k_mbf.onnx` — ArcFace face recognizer.
  These model files must be available under the Indexer's output directory, normally `SemanticImageSearch.Indexer/bin/Debug/net10.0/MLModels` when running from Visual Studio or `dotnet run`.
- A `ReferenceFaces` directory containing labeled reference images. See the face-recognition section below for the naming convention.
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
| `query` | conditional | Text describing what you're looking for. Optional when `people`, `from`, or `to` is supplied. |
| `limit` | no | Number of results, 1–100 (default 10). |
| `from` | no | Earliest date taken, `yyyy-MM-dd`, inclusive. |
| `to` | no | Latest date taken, `yyyy-MM-dd`, inclusive of the whole day. |
| `people` | no | Repeat this parameter for each person. Images must contain all selected people. |

Example:

```
GET /api/search?query=dog%20on%20the%20beach&people=Alice&from=2023-01-01&to=2023-12-31&limit=10
```

People/date-only search:

```
GET /api/search?people=Alice&people=Bob&limit=10
```

Response:

```json
[
  {
	"id": "011caa7b-b80a-4408-8ab1-2c73d8fc22da",
	"filePath": "D:\\Media\\images\\...\\IMG_1234.jpg",
	"description": "A brown dog runs along a sandy beach next to the water...",
	"peopleNames": ["Alice", "Bob"],
	"dateTaken": "2023-07-04T18:22:10.0000000Z",
	"score": 0.78
  }
]
```

`score` is the cosine similarity (higher is closer). Filter-only results have a score of `0`. The request returns `400` if no query or filter is provided, or if `from` is after `to`.

### `GET /api/people`

Returns a sorted, distinct list of recognized people found in the indexed image payloads. Names come from the subfolders or filenames in the Indexer's `ReferenceFaces` directory.

### `GET /api/images/{id}`

Returns the image file for a Qdrant point ID, with the correct content type. Returns `404` if the point or the file doesn't exist. Lookup is by ID only, so the browser can't request arbitrary files from disk.

Sample requests for the API endpoints are in `SemanticImageSearch.API/SemanticImageSearch.API.http`.

## Data stored in Qdrant

Collection `image_library`, 768-dimension vectors, cosine distance. Each point's payload contains:

| Field | Description |
|---|---|
| `file_path` | Full path to the image on disk. |
| `description` | The LLaVA caption that was embedded. |
| `people_names` | Array of recognized reference-face names. This is independent from `description`. |
| `date_taken` | Capture date in UTC (ISO 8601). Comes from EXIF `DateTimeOriginal`, then `DateTimeDigitized`, then `DateTime`, and falls back to the file's creation time. EXIF times have no time zone, so they're treated as local time. |
| `last_modified_utc` | File's last-write time in UTC. |

A datetime payload index is created on `date_taken` to keep date-range filtering fast.

## Configuration

Settings live in `appsettings.json` for the Indexer and the API. Both share the `Qdrant` and `Ollama` sections. Keep `CollectionName` and `EmbeddingModel` identical in both, or searches won't match what was indexed.

| Key | Used by | Default |
|---|---|---|
| `Indexer:PictureLibraryPath` | Indexer | `D:\Media\images\Random Kid Pics` |
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
| Face test target | Indexer (`Program.cs`) | `D:\Media\images\Natis Phone 4_23_16` (first 100 images) |
| Face match threshold | Indexer (`Program.cs`) | `0.45` |

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

The Indexer also runs its current face-recognition batch test before the normal indexing pass. That test uses the hard-coded target folder shown in the configuration table and reports matches to the console; the configurable `Indexer:PictureLibraryPath` is used by the subsequent indexing pass. Face recognition requires the ONNX models and `ReferenceFaces` directory described above.

Because existing files are skipped by path, images indexed before `people_names` was stored as an array will not automatically receive the new people data. Delete the collection and re-index if you need to populate people data for existing images.

To re-index everything, for example after changing the caption prompt:

1. Delete the `image_library` collection in the Qdrant dashboard (<http://localhost:6333/dashboard>), or with:
   ```powershell
   Invoke-RestMethod -Method Delete http://localhost:6333/collections/image_library
   ```
2. Run the Indexer again.

## Limitations and ideas

- Images are assumed not to change once indexed. Edited files aren't re-captioned, and deleted or moved files aren't removed from the collection (a moved file is indexed again under its new path).
- The face-recognition batch test currently uses a separate hard-coded folder and processes up to 100 images before the normal configurable indexing pass.
- The API reads images straight from their original paths, so it must run on the same machine as the photo library (or with the same paths mounted).
- Captions are only as good as LLaVA's interpretation of each photo. Text search matches the caption, not the pixels.
