namespace SemanticImageSearch.Core.Interfaces
{
    public interface IFaceDetector
    {
        Task<List<FaceBox>> DetectFacesAsync(string imagePath, CancellationToken cancellationToken = default);


    }
}