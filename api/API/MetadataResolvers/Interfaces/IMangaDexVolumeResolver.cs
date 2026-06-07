using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using API.Schema.SeriesContext;

namespace API.MetadataResolvers.Interfaces;

public interface IMangaDexVolumeResolver
{
    Task<Dictionary<string, int>> GetChapterToVolumeMapAsync(Series manga, CancellationToken cancellationToken = default);
}
