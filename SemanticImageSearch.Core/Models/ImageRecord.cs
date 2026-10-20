using Qdrant.Client.Grpc;
using System;
using System.Collections.Generic;
using System.Text;

namespace SemanticImageSearch.Core.Models
{
    public class ImageRecord
    {
        public string FilePath { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public float[] Vector { get; set; } = Array.Empty<float>();
        public DateTime LastModifiedUtc { get; set; }
        public DateTime DateTaken { get; set; }
    }
}
