# Semantic Image Search

Search your own photo library by describing what's in the picture, for example *"dog playing on the beach"* or *"birthday cake with candles"*, narrowed down by **who is in the photo** and **when it was taken**.

Everything runs locally, and no photo leaves your machine:

- A vision-language model (**LLaVA**, served by **Ollama**) writes a factual caption for every photo.
- The caption is turned into a 768-dimension vector with **nomic-embed-text** and stored in the **Qdrant** vector database.
- **SCRFD** finds faces and **ArcFace** turns each one into a 512-dimension face embedding. These are matched against a small set of labelled reference photos to tag known people.
- A search embeds your text the same way and asks Qdrant for the nearest captions, with optional people and date filters.

The stack is .NET 10 (console app, class library, ASP.NET Core Web API), ONNX Runtime and ImageSharp for the face models, and React 19 + TypeScript + Vite for the UI.

## How it works

```
                      INDEXING  (SemanticImageSearch.Indexer, console app)

   photo ──┬──▶ Ollama: llava ──caption──▶ Ollama: nomic-embed-text ──768-d vector──┐
           │                                                                        │
           ├──▶ SCRFD (ONNX) ──face boxes + landmarks──▶ ArcFace (ONNX)             │
           │                                               │ 512-d face embeddings  │
           │                                               ▼                        │
           │                    FaceRegistry (from ReferenceFaces/) ──names─────────┤
           │                                                                        │
           └──▶ EXIF reader ──date_taken────────────────────────────────────────────┤
                                                                                    ▼
                                                              ┌──────────────────────────┐
                                                              │ Qdrant: image_library    │
                                                              │ vector + payload         │
                                                              │ (file_path, description, │
                                                              │  people_names,           │
                                                              │  date_taken, ...)        │
                                                              └──────────────────────────┘
                                                                            ▲
                      SEARCHING                                             │ vector query
                                                                            │ + payload filters
   ┌────────────┐  /api/search, /api/people   ┌──────────────┐              │
   │  React UI  │ ──────────────────────────▶ │  ASP.NET API │ ─────────────┘
   │   (Vite)   │ ◀────────────────────────── │              │ ──query text──▶ Ollama: nomic-embed-text
   └────────────┘  results, /api/images/{id}  └──────────────┘
```

1. **Indexing:** the Indexer scans a folder (including subfolders) for images. For each new image it:
   - asks LLaVA for a short, factual description (the prompt avoids mood, opinions and speculation so captions stay searchable);
   - detects faces and matches them against the reference faces to get a list of known people;
   - embeds the caption with `nomic-embed-text` (768 dimensions);
   - reads the capture date from EXIF;
   - upserts the vector and its metadata into Qdrant.
2. **Searching:** the API embeds the search text with the same model and asks Qdrant for the nearest vectors by cosine similarity. Qdrant applies the date-range and people filters during the vector search, not afterwards.
3. **Viewing:** the UI shows the results as a thumbnail grid with the date taken, the similarity score, the recognized people and the caption. Images are served by their Qdrant point ID, never by file path.

## Face detection and recognition pipeline

Face recognition runs while indexing, alongside captioning. It is deliberately kept separate from the caption: names go into their own `people_names` field and are never added to the LLaVA description, so the text embedding describes what's in the picture, not who.

Both models run in-process through **ONNX Runtime**, with image preprocessing done in **ImageSharp**. No Python or external face service is needed.

1. **Detection (`ScrfdFaceDetector`, `MLModels/2.5g_bnkps.onnx`):**
   - The image is resized to 640×640 and normalized as `(pixel − 127.5) / 128`.
   - SCRFD outputs scores, box distances and 5 facial keypoints at three feature-map strides (8, 16, 32). The detector decodes these back into image coordinates.
   - Detections below 0.5 confidence are dropped, and overlapping boxes are merged with non-maximum suppression (IoU 0.4).
2. **Embedding (`ArcFaceRecognizer`, `MLModels/w600k_mbf.onnx`):**
   - Each face is cropped with a 15% margin.
   - If the eye landmarks show the head tilted by more than 1.5°, the crop is rotated level.
   - The crop is resized to 112×112, converted to BGR and normalized as `(pixel − 127.5) / 127.5`.
   - The model outputs a 512-dimension vector, which is L2-normalized so a plain dot product gives the cosine similarity.
3. **Reference registry (`FaceRegistry`):** on startup the Indexer loads every image in the `ReferenceFaces` folder at the repository root, takes the highest-confidence face in each, and stores its embedding under a person's name. The name comes from the image's subfolder or, for images placed directly in `ReferenceFaces`, from the filename:

   ```text
   ReferenceFaces/
   ├── Alice/
   │   ├── alice-1.jpg
   │   └── alice-2.jpg
   ├── Bob/
   │   └── bob-1.jpg
   └── Charlie.jpg
   ```

   This registers `Alice` (two embeddings), `Bob` and `Charlie`. Several reference photos per person improve matching across poses, ages and lighting. The folder's contents are git-ignored because they are personal photos.
4. **Matching:** each face found in a photo is compared with every reference embedding, and the closest one wins. If its similarity is at least **0.45**, that person's name is recorded. Names are de-duplicated per image and stored as an array in `people_names`. Faces below the threshold are left unnamed.
5. **Searching by people:** `GET /api/people` lists the distinct names found in the index, and the UI shows them in a multi-select. The selected names are sent as repeated `people` parameters, and an image must contain **all** of them. Searches with only people and/or dates skip Ollama entirely and run as pure Qdrant payload filters.

## Projects

| Project | Type | Purpose |
|---|---|---|
| `SemanticImageSearch.Core` | Class library (.NET 10) | Shared models, options and services: `OllamaAIService` (captions and embeddings), `QdrantVectorService` (collection, payload indexes, upserts, indexed-path lookup), `ExifDateReader`, `ScrfdFaceDetector`, `ArcFaceRecognizer` and `FaceRegistry`. Also contains the bundled ONNX models. |
| `SemanticImageSearch.Indexer` | Console app (.NET 10) | Loads the reference faces, scans the picture folder, and captions, face-tags, embeds and indexes each new image. |
| `SemanticImageSearch.API` | ASP.NET Core Web API (.NET 10) | `GET /api/search`, `GET /api/people` and `GET /api/images/{id}`. |
| `SemanticImageSearch.Web` | React 19 + TypeScript + Vite | Search UI with text, people, date-range and result-count controls, plus a thumbnail grid. |

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
- Optional: one or more labelled photos in `ReferenceFaces/` (see above). Without them, indexing and text search still work, but no people are tagged.

The face models (`2.5g_bnkps.onnx` and `w600k_mbf.onnx`) are included in `SemanticImageSearch.Core/MLModels` and copied to the Indexer's output folder on build, so there is nothing to download. They are pretrained [InsightFace](https://github.com/deepinsight/insightface) models, which InsightFace releases for non-commercial research use.

## Getting started

### 1. Point the Indexer at your photos

Set `Indexer:PictureLibraryPath` in `SemanticImageSearch.Indexer/appsettings.json`:

```json
"Indexer": {
  "PictureLibraryPath": "C:\\Path\\To\\Your\\Photos"
}
```

Or pass it on the command line (see below). Subfolders are included. Supported formats are `.jpg`, `.jpeg`, `.png` and `.webp` (configurable through `Indexer:SupportedExtensions`).

### 2. Index the images

Run from the repository root so the Indexer finds `ReferenceFaces/`:

```powershell
dotnet run --project SemanticImageSearch.Indexer

# or override the folder for this run
dotnet run --project SemanticImageSearch.Indexer -- --path "C:\Path\To\Your\Photos"
```

The Indexer registers the reference faces, creates the `image_library` collection and its payload indexes if needed, then prints each caption as it goes. A failure on one image is logged and skipped, so it doesn't stop the run.

#### Optional: face-matching diagnostic

To tune the match threshold or check your reference photos, set `Indexer:FaceTestPath`. Before indexing, the Indexer then scans up to `Indexer:FaceTestLimit` images from that folder and prints, for every face it finds, the best match and its similarity, followed by a per-person summary. When `FaceTestPath` is empty (the default), the diagnostic is skipped.

Machine-specific paths like this belong in `SemanticImageSearch.Indexer/appsettings.Local.json`. The Indexer loads that file after `appsettings.json`, and git ignores it:

```json
{
  "Indexer": {
    "FaceTestPath": "D:\\Path\\To\\A\\Sample\\Folder",
    "FaceTestLimit": 100
  }
}
```

### 3. Start the API

```powershell
dotnet run --project SemanticImageSearch.API --launch-profile http
```

The API listens on <http://localhost:5256>. In the Development environment, its OpenAPI document is available at <http://localhost:5256/openapi/v1.json>.

### 4. Start the UI

```powershell
cd SemanticImageSearch.Web
npm install
npm run dev
```

Open <http://localhost:54879>. The Vite dev server forwards `/api` requests to the API.

> **PowerShell tip:** if `npm` fails with *"running scripts is disabled on this system"*, use `npm.cmd` instead (for example `npm.cmd run dev`).

### Running from Visual Studio

Right-click the solution, choose **Configure Startup Projects**, then **Multiple startup projects**. Set `SemanticImageSearch.API` and `SemanticImageSearch.Web` to **Start**, then press F5. Run the Indexer separately whenever you want to index.

## API reference

### `GET /api/search`

| Parameter | Required | Description |
|---|---|---|
| `query` | conditional | Text describing what you're looking for. Optional when `people`, `from` or `to` is supplied. |
| `limit` | no | Number of results, clamped to 1–100 (default 10). |
| `from` | no | Earliest date taken, `yyyy-MM-dd`, inclusive. |
| `to` | no | Latest date taken, `yyyy-MM-dd`, inclusive of the whole day. |
| `people` | no | Repeat once per person. Images must contain all of the selected people. Names must match exactly as returned by `/api/people`. |

Text search with filters:

```
GET /api/search?query=dog%20on%20the%20beach&people=Alice&from=2023-01-01&to=2023-12-31&limit=10
```

Filter-only search (no Ollama call):

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

- `score` is the cosine similarity between the query and the caption (higher means closer).
- Filter-only searches aren't ranked: they return matches in Qdrant's storage order with a score of `0`.
- The request returns `400` if there is no query and no filter, or if `from` is after `to`, and `500` with an `{ "error": "..." }` body if Qdrant or Ollama fails.
- Date filters are interpreted as calendar days in the API server's local time zone, which matches how EXIF dates are stored (see below).

### `GET /api/people`

Returns a sorted, distinct list of every name stored in `people_names` across the collection. The UI uses it to fill the People filter.

### `GET /api/images/{id}`

Returns the image file for a Qdrant point ID, with the correct content type and a one-hour cache header. Returns `404` if the point doesn't exist, the extension isn't a supported image type, or the file is no longer on disk. Because lookup is by ID only, the browser can't ask for arbitrary files.

Sample requests for every endpoint are in `SemanticImageSearch.API/SemanticImageSearch.API.http`.

## Data stored in Qdrant

The `image_library` collection holds 768-dimension vectors with cosine distance. Each point's ID is a GUID derived from an MD5 hash of the file's normalized full path, and its payload contains:

| Field | Description |
|---|---|
| `file_path` | Full path to the image on disk. |
| `description` | The LLaVA caption that was embedded. |
| `people_names` | Array of recognized reference-face names. Kept separate from `description`. |
| `date_taken` | Capture date in UTC (ISO 8601). Taken from EXIF `DateTimeOriginal`, then `DateTimeDigitized`, then `DateTime`, falling back to the file's creation time. EXIF times have no time zone, so they are treated as local time before converting to UTC. |
| `last_modified_utc` | The file's last-write time in UTC. |

Payload indexes are created on `date_taken` (datetime) and `people_names` (keyword) so that filtered searches stay fast as the library grows.

## Configuration

The Indexer and the API each have their own `appsettings.json`, and both share the `Qdrant` and `Ollama` sections. Keep `CollectionName` and `EmbeddingModel` identical in both, or searches won't match what was indexed.

| Key | Used by | Default |
|---|---|---|
| `Indexer:PictureLibraryPath` | Indexer | `D:\Media\images\Random Kid Pics` |
| `Indexer:SupportedExtensions` | Indexer | `.jpg`, `.jpeg`, `.png`, `.webp` |
| `Indexer:FaceTestPath` | Indexer | empty (face-matching diagnostic skipped) |
| `Indexer:FaceTestLimit` | Indexer | `100` |
| `Qdrant:Host` / `Qdrant:Port` | Indexer, API | `localhost` / `6334` (gRPC) |
| `Qdrant:CollectionName` | Indexer, API | `image_library` |
| `Qdrant:VectorSize` | Indexer | `768` (must match the embedding model) |
| `Ollama:BaseUrl` | Indexer, API | `http://localhost:11434` |
| `Ollama:VisionModel` | Indexer | `llava` |
| `Ollama:EmbeddingModel` | Indexer, API | `nomic-embed-text` |
| `Ollama:Temperature` | Indexer | `0.2` |
| `Ollama:DescriptionPrompt` | Indexer | Factual, 2–4 sentences (see `OllamaOptions.cs`) |
| `Cors:AllowedOrigins` | API | `http://localhost:54879`, `https://localhost:54879` |

These face-recognition values are currently constants in code rather than settings:

| Setting | Location | Value |
|---|---|---|
| Face match threshold | `SemanticImageSearch.Indexer/Program.cs` | `0.45` cosine similarity |
| Detection confidence / NMS IoU | `ScrfdFaceDetector` constructor | `0.5` / `0.4` |
| Reference faces folder | `Program.cs` | `ReferenceFaces/` in the working directory, otherwise the repository root |

### Overriding settings

Later sources override earlier ones:

1. `appsettings.json`
2. `appsettings.Local.json` (Indexer only; git-ignored, for machine-specific paths)
3. Environment variables. The Indexer uses a `SIS_` prefix, for example `SIS_Indexer__PictureLibraryPath`. The API uses standard ASP.NET Core names, for example `Qdrant__Host`.
4. Command-line arguments, for example `--Qdrant:Host myserver`. The Indexer also accepts `--path` / `-p` as a shortcut for `--Indexer:PictureLibraryPath`.

Examples:

```powershell
dotnet run --project SemanticImageSearch.Indexer -- --path "E:\Photos" --Ollama:VisionModel llava:13b
dotnet run --project SemanticImageSearch.API --launch-profile http -- --Qdrant:Host qdrant.local
```

### Ports

These are set in their standard tooling files:

| Setting | Location | Default |
|---|---|---|
| API port | `SemanticImageSearch.API/Properties/launchSettings.json` | `5256` (`https` profile adds `7122`) |
| UI port and API proxy | `SemanticImageSearch.Web/vite.config.ts` | `54879` → `http://localhost:5256` |

## Incremental indexing and re-indexing

At startup the Indexer reads every `file_path` already stored in Qdrant and skips those files, so only newly added images are captioned and face-tagged. Because the point ID is derived from the file path, the same file always maps to the same point and can never be stored twice.

Already-indexed images are not reprocessed. To re-index everything, for example after changing the caption prompt, the threshold or the reference faces:

1. Delete the `image_library` collection in the Qdrant dashboard (<http://localhost:6333/dashboard>), or with:
   ```powershell
   Invoke-RestMethod -Method Delete http://localhost:6333/collections/image_library
   ```
2. Run the Indexer again.

## Design decisions

- **Caption-then-embed instead of image embeddings.** Storing a readable caption makes every result explainable (you can see *why* it matched) and lets search use a strong text-embedding model. The trade-off is that search is only as good as the caption.
- **People as structured data, not text.** Putting names in the caption would weaken the semantic embedding and make name matching fuzzy. A keyword-indexed array gives exact "all of these people" filtering instead.
- **Filters inside the vector query.** Date and people conditions are sent as a Qdrant filter, so the top-N results are the top-N *matching* images rather than a post-filtered subset.
- **No embedding call for filter-only searches.** Browsing by person or date doesn't touch Ollama at all.
- **Deterministic point IDs.** Hashing the path makes indexing idempotent and lets the Indexer resume after an interruption.
- **Safe file serving.** The browser never sends a path; the API resolves the path from Qdrant and only serves known image types.
- **In-process face models.** ONNX Runtime keeps face recognition inside the .NET process, with no Python sidecar or cloud API.

## Limitations and ideas

- Images are assumed not to change once indexed. Edited files aren't re-captioned, deleted or moved files aren't removed from the collection, and a moved file is indexed again under its new path.
- Adding or changing reference faces doesn't update images that are already indexed. Re-index (see above) to apply them.
- The face threshold and detector settings are code constants rather than configuration.
- SCRFD input is stretched to 640×640 rather than letterboxed, which can reduce detection accuracy on very wide or tall images.
- The API reads images straight from their original paths, so it must run on the same machine as the photo library (or with the same paths mounted).
- Captions are only as good as LLaVA's reading of each photo. Text search matches the caption, not the pixels.
