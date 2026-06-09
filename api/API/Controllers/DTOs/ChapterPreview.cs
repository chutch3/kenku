using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.DTOs;

/// <summary>A chapter as reported live by a connector, before anything is saved — what the add flow
/// shows so 'this source yields 0 chapters' is visible while a different source can still be picked.</summary>
public record ChapterPreview(string ChapterNumber, int? VolumeNumber, string? Title)
{
    [Required] public string ChapterNumber { get; init; } = ChapterNumber;
    [Description("Volume the connector assigns this chapter to, if any.")]
    public int? VolumeNumber { get; init; } = VolumeNumber;
    public string? Title { get; init; } = Title;
}
