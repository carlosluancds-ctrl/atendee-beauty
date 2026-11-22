namespace Atendee.Application.Abstractions;

/// <summary>
/// Abstraction for sending transactional emails.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends a transactional email.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="htmlBody">HTML body content.</param>
    /// <param name="textBody">Plain text body content (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if email was accepted for delivery.</returns>
    Task<bool> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an appointment confirmation email to the customer.
    /// </summary>
    Task<bool> SendAppointmentConfirmationAsync(
        string to,
        string customerName,
        string serviceName,
        string professionalName,
        DateTimeOffset startAtUtc,
        int durationMinutes,
        string tenantTimeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an appointment cancellation email to the customer.
    /// </summary>
    Task<bool> SendAppointmentCancellationAsync(
        string to,
        string customerName,
        string serviceName,
        string professionalName,
        DateTimeOffset startAtUtc,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an appointment reschedule email to the customer.
    /// </summary>
    Task<bool> SendAppointmentRescheduleAsync(
        string to,
        string customerName,
        string serviceName,
        string professionalName,
        DateTimeOffset oldStartAtUtc,
        DateTimeOffset newStartAtUtc,
        int durationMinutes,
        string tenantTimeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an appointment reminder email to the customer.
    /// </summary>
    Task<bool> SendAppointmentReminderAsync(
        string to,
        string customerName,
        string serviceName,
        string professionalName,
        DateTimeOffset startAtUtc,
        int durationMinutes,
        string tenantTimeZoneId,
        CancellationToken cancellationToken = default);
}