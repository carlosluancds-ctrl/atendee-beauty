namespace Atendee.Application.Models;

public sealed record CustomerPageModel(
    IReadOnlyList<CustomerModel> Items,
    int Page,
    int PageSize,
    int TotalCount);
