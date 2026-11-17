namespace Atendee.Domain.Tenants;

public enum TenantStatus
{
    PendingVerification = 0,
    SetupRequired = 1,
    Trial = 2,
    Active = 3,
    PastDue = 4,
    Suspended = 5,
    Closing = 6,
    Closed = 7
}
