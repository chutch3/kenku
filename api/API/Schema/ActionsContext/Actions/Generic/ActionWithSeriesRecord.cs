using System.ComponentModel.DataAnnotations;

namespace API.Schema.ActionsContext.Actions.Generic;

public interface IActionWithSeriesRecord
{
    /// <summary>
    /// <see cref="Schema.SeriesContext.Series"/> for which the cover was downloaded
    /// </summary>
    [StringLength(64)]
    public string SeriesId { get; init; }
}