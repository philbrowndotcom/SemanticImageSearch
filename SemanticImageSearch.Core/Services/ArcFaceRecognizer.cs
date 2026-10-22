using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SemanticImageSearch.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SemanticImageSearch.Core.Services;

public class ArcFaceRecognizer : IFaceRecognizer, IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private const int TargetSize = 112;

    public ArcFaceRecognizer(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"ArcFace model not found at: {modelPath}");
        }

        _session = new InferenceSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public async Task<float[]> GenerateFaceEmbeddingAsync(string imagePath, FaceBox face, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var image = Image.Load<Rgb24>(imagePath);
            return GenerateFaceEmbedding(image, face);
        }, cancellationToken);
    }

    public float[] GenerateFaceEmbedding(Image<Rgb24> image, FaceBox face)
    {
        // 1. Calculate tilt angle from eyes if landmarks are available
        float angleDegrees = 0f;
        if (face.Landmarks != null && face.Landmarks.Length >= 4)
        {
            float lx = face.Landmarks[0];
            float ly = face.Landmarks[1];
            float rx = face.Landmarks[2];
            float ry = face.Landmarks[3];
            float dx = rx - lx;
            float dy = ry - ly;
            angleDegrees = MathF.Atan2(dy, dx) * (180.0f / MathF.PI);
        }

        // 2. Crop face with ~15% padding margin to capture the full face contour
        float width = Math.Max(1, face.X2 - face.X1);
        float height = Math.Max(1, face.Y2 - face.Y1);
        float marginX = width * 0.15f;
        float marginY = height * 0.15f;

        int cropX = (int)Math.Max(0, face.X1 - marginX);
        int cropY = (int)Math.Max(0, face.Y1 - marginY);
        int cropW = (int)Math.Min(image.Width - cropX, width + marginX * 2);
        int cropH = (int)Math.Min(image.Height - cropY, height + marginY * 2);

        if (cropW <= 0 || cropH <= 0)
        {
            return new float[512];
        }

        using var faceCrop = image.Clone(ctx =>
        {
            ctx.Crop(new Rectangle(cropX, cropY, cropW, cropH));

            // Straighten if head tilt is noticeable
            if (MathF.Abs(angleDegrees) > 1.5f)
            {
                ctx.Rotate(-angleDegrees);
            }

            ctx.Resize(TargetSize, TargetSize);
        });

        // 3. Build input tensor [1, 3, 112, 112] in BGR order with standard InsightFace normalization
        var inputTensor = new DenseTensor<float>([1, 3, TargetSize, TargetSize]);
        faceCrop.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    // ArcFace BGR order: (pixel - 127.5) / 127.5
                    inputTensor[0, 0, y, x] = (pixelRow[x].B - 127.5f) / 127.5f;
                    inputTensor[0, 1, y, x] = (pixelRow[x].G - 127.5f) / 127.5f;
                    inputTensor[0, 2, y, x] = (pixelRow[x].R - 127.5f) / 127.5f;
                }
            }
        });

        // 4. Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
        };

        using var results = _session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        float[] embedding = new float[512];
        if (outputTensor is DenseTensor<float> dt)
        {
            dt.Buffer.Span.Slice(0, Math.Min(512, dt.Buffer.Length)).CopyTo(embedding);
        }
        else
        {
            int i = 0;
            foreach (var val in outputTensor)
            {
                if (i < 512) embedding[i++] = val;
            }
        }

        // 5. L2-Normalize embedding to unit sphere
        float sumSquares = 0f;
        for (int i = 0; i < embedding.Length; i++)
        {
            sumSquares += embedding[i] * embedding[i];
        }

        float norm = MathF.Sqrt(sumSquares);
        if (norm > 1e-6f)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= norm;
            }
        }

        return embedding;
    }

    public float CompareFaces(float[] embeddingA, float[] embeddingB)
    {
        if (embeddingA.Length != embeddingB.Length)
        {
            throw new ArgumentException("Face embeddings must have the same dimension to be compared.");
        }

        // Dot product of two unit-normalized vectors equals cosine similarity
        float dotProduct = 0f;
        for (int i = 0; i < embeddingA.Length; i++)
        {
            dotProduct += embeddingA[i] * embeddingB[i];
        }

        return dotProduct;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}

