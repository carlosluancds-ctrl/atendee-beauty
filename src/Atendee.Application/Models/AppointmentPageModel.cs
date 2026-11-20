namespace Atendee.Application.Models;

public sealed record AppointmentPageModel(
    IReadOnlyList<AppointmentModel> Items,
    int Page,
    int PageSize,
    int TotalCount);