using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SemanticImageSearch.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SemanticImageSearch.Core.Services;

public class ScrfdFaceDetector : IFaceDetector, IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private const int TargetSize = 640;
    private readonly float _scoreThreshold;
    private readonly float _nmsThreshold;

    // Standard SCRFD detection strides
    private readonly int[] _strides = [8, 16, 32];

    public ScrfdFaceDetector(string modelPath, float scoreThreshold = 0.5f, float nmsThreshold = 0.4f)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"SCRFD model file not found at: {modelPath}");
        }

        _scoreThreshold = scoreThreshold;
        _nmsThreshold = nmsThreshold;
        _session = new InferenceSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public async Task<List<FaceBox>> DetectFacesAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var image = Image.Load<Rgb24>(imagePath);
            int originalWidth = image.Width;
            int originalHeight = image.Height;

            // Resize image to 640x640 as expected by SCRFD
            using var resizedImage = image.Clone(x => x.Resize(TargetSize, TargetSize));

            // Create input tensor [1, 3, 640, 640]
            var inputTensor = new DenseTensor<float>([1, 3, TargetSize, TargetSize]);

            resizedImage.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++)
                    {
                        // Standard SCRFD normalization: mean = 127.5, std = 128.0
                        inputTensor[0, 0, y, x] = (pixelRow[x].R - 127.5f) / 128.0f;
                        inputTensor[0, 1, y, x] = (pixelRow[x].G - 127.5f) / 128.0f;
                        inputTensor[0, 2, y, x] = (pixelRow[x].B - 127.5f) / 128.0f;
                    }
                }
            });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
            };

            using var results = _session.Run(inputs);
            var outputList = results.ToList();

            var rawBoxes = new List<FaceBox>();
            int fmc = _strides.Length; // 3 pyramid levels
            bool hasKps = outputList.Count >= fmc * 3;

            // InsightFace outputs are grouped by task:
            // 0..fmc-1: scores, fmc..2*fmc-1: bboxes, 2*fmc..3*fmc-1: kps
            for (int s = 0; s < fmc; s++)
            {
                int stride = _strides[s];
                var scoreSpan = ((DenseTensor<float>)outputList[s].AsTensor<float>()).Buffer.Span;
                var bboxSpan = ((DenseTensor<float>)outputList[s + fmc].AsTensor<float>()).Buffer.Span;
                var kpsSpan = hasKps ? ((DenseTensor<float>)outputList[s + fmc * 2].AsTensor<float>()).Buffer.Span : ReadOnlySpan<float>.Empty;

                int featureHeight = TargetSize / stride;
                int featureWidth = TargetSize / stride;
                int totalGridPoints = featureHeight * featureWidth;
                int numAnchors = scoreSpan.Length / totalGridPoints;

                float scaleX = (float)originalWidth / TargetSize;
                float scaleY = (float)originalHeight / TargetSize;

                for (int ah = 0; ah < featureHeight; ah++)
                {
                    for (int aw = 0; aw < featureWidth; aw++)
                    {
                        for (int anchorIdx = 0; anchorIdx < numAnchors; anchorIdx++)
                        {
                            int index = (ah * featureWidth + aw) * numAnchors + anchorIdx;
                            if (index >= scoreSpan.Length) continue;

                            float score = scoreSpan[index];
                            if (score < _scoreThreshold) continue;

                            float cx = aw * stride;
                            float cy = ah * stride;

                            int bboxOffset = index * 4;
                            float l = bboxSpan[bboxOffset];
                            float t = bboxSpan[bboxOffset + 1];
                            float r = bboxSpan[bboxOffset + 2];
                            float b = bboxSpan[bboxOffset + 3];

                            float x1 = cx - l * stride;
                            float y1 = cy - t * stride;
                            float x2 = cx + r * stride;
                            float y2 = cy + b * stride;

                            float[]? landmarks = null;
                            if (hasKps && (index * 10 + 9) < kpsSpan.Length)
                            {
                                landmarks = new float[10];
                                int kpsOffset = index * 10;
                                for (int k = 0; k < 5; k++)
                                {
                                    float kx = cx + kpsSpan[kpsOffset + k * 2] * stride;
                                    float ky = cy + kpsSpan[kpsOffset + k * 2 + 1] * stride;
                                    landmarks[k * 2] = Math.Clamp(kx * scaleX, 0, originalWidth);
                                    landmarks[k * 2 + 1] = Math.Clamp(ky * scaleY, 0, originalHeight);
                                }
                            }

                            rawBoxes.Add(new FaceBox(
                                Math.Clamp(x1 * scaleX, 0, originalWidth),
                                Math.Clamp(y1 * scaleY, 0, originalHeight),
                                Math.Clamp(x2 * scaleX, 0, originalWidth),
                                Math.Clamp(y2 * scaleY, 0, originalHeight),
                                score,
                                landmarks
                            ));
                        }
                    }
                }
            }

            return ApplyNms(rawBoxes, _nmsThreshold);
        }, cancellationToken);
    }

    private static float Sigmoid(float x) => 1.0f / (1.0f + MathF.Exp(-x));

    private static List<FaceBox> ApplyNms(List<FaceBox> boxes, float threshold)
    {
        var sorted = boxes.OrderByDescending(b => b.Confidence).ToList();
        var selected = new List<FaceBox>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            selected.Add(best);
            sorted.RemoveAt(0);

            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                if (CalculateIoU(best, sorted[i]) > threshold)
                {
                    sorted.RemoveAt(i);
                }
            }
        }

        return selected;
    }

    private static float CalculateIoU(FaceBox a, FaceBox b)
    {
        float x1 = Math.Max(a.X1, b.X1);
        float y1 = Math.Max(a.Y1, b.Y1);
        float x2 = Math.Min(a.X2, b.X2);
        float y2 = Math.Min(a.Y2, b.Y2);

        float interArea = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        if (interArea == 0) return 0;

        float areaA = (a.X2 - a.X1) * (a.Y2 - a.Y1);
        float areaB = (b.X2 - b.X1) * (b.Y2 - b.Y1);

        return interArea / (areaA + areaB - interArea);
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
