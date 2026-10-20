using System;
using System.Collections.Generic;
using System.Text;

namespace SemanticImageSearch.Core.Interfaces
{
    public interface IVectorDatabaseService
    {
        Task EnsureCollectionExistsAsync(ulong vectorSize);
        Task UpsertImageAsync(Models.ImageRecord record);
    }
}
