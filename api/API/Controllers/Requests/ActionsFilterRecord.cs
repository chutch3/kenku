using API.Schema.ActionsContext.Actions;

namespace API.Controllers.Requests;

public sealed record ActionsFilterRecord(DateTime? Start, DateTime? End, string? SeriesId, string? ChapterId, Actions? Action);