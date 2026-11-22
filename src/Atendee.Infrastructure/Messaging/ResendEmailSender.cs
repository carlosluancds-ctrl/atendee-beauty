using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Atendee.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atendee.Infrastructure.Messaging;

/// <summary>
/// Resend.com email sender implementation.
/// Free tier: 3,000 emails/month.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ResendEmailSender> _logger;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public ResendEmailSender(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ResendEmailSender> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var apiKey = configuration["Resend:ApiKey"] ?? configuration["RESEND_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Resend API key not configured. Set Resend:ApiKey or RESEND_API_KEY.");

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _fromEmail = configuration["Resend:FromEmail"] ?? "Atendee Beauty <noreply@atendee.local>";
        _fromName = configuration["Resend:FromName"] ?? "Atendee Beauty";
    }

    public async Task<bool> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            from = _fromEmail,
            to = new[] { to },
            subject = subject,
            html = htmlBody,
            text = textBody
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("https://api.resend.com/emails", payload, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent to {To} via Resend: {Subject}", to, subject);
                return true;
            }

            _logger.LogError("Failed to send email via Resend to {To}: {StatusCode} - {Content}", to, response.StatusCode, content);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending email via Resend to {To}: {Subject}", to, subject);
            return false;
        }
    }

    public Task<bool> SendAppointmentConfirmationAsync(
        string to,
        string customerName,
        string serviceName,
        string professionalName,
        DateTimeOffset startAtUtc,
        int durationMinutes,
        string tenantTimeZoneId,
        CancellationToken cancellationToken = default)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(tenantTimeZoneId);
        var localStart = TimeZoneInfo.ConvertTime(startAtUtc, zone);
        var localEnd = localStart.AddMinutes(durationMinutes);

        var subject = $"Confirmação: {serviceName} com {professionalName}";
        var htmlBody = $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"></head>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                <div style="background: #2a5f9c; color: white; padding: 20px; border-radius: 8px 8px 0 0;">
                    <h1 style="margin: 0;">Atendee Beauty</h1>
                    <p style="margin: 10px 0 0;">Agendamento Confirmado</p>
                </div>
                <div style="background: #f9f9f9; padding: 20px; border: 1px solid #ddd; border-top: none; border-radius: 0 0 8px 8px;">
                    <p>Olá <strong>{customerName}</strong>,</p>
                    <p>Seu agendamento foi <strong>confirmado</strong> com sucesso!</p>
                    <table style="width: 100%; border-collapse: collapse; margin: 20px 0;">
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Serviço</td><td style="padding: 10px;">{serviceName}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Profissional</td><td style="padding: 10px;">{professionalName}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Data</td><td style="padding: 10px;">{localStart:dd/MM/yyyy}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Horário</td><td style="padding: 10px;">{localStart:HH:mm} - {localEnd:HH:mm}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Duração</td><td style="padding: 10px;">{durationMinutes} minutos</td></tr>
                    </table>
                    <p>Se precisar cancelar ou reagendar, use o link enviado pelo salão (mínimo 24h de antecedência).</p>
                    <hr style="border: none; border-top: 1px solid #ddd; margin: 20px 0;">
                    <p style="font-size: 12px; color: #666;">Atendee Beauty - Sua recepcionista IA para salões e clínicas de estética.</p>
                </div>
            </body>
            </html>
            """;

        var textBody = $"""
            Olá {customerName},

            Seu agendamento foi confirmado!

            Serviço: {serviceName}
            Profissional: {professionalName}
            Data: {localStart:dd/MM/yyyy}
            Horário: {localStart:HH:mm} - {localEnd:HH:mm}
            Duração: {durationMinutes} minutos

            Se precisar cancelar ou reagendar, use o link enviado pelo salão (mínimo 24h de antecedência).

            Atendee Beauty
            """;

        return SendAsync(to, subject, htmlBody, textBody, cancellationToken);
    }

    public Task<bool> SendAppointmentCancellationAsync(
        string to,
        string customerName,
        string serviceName,
        string professionalName,
        DateTimeOffset startAtUtc,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var subject = $"Cancelado: {serviceName} com {professionalName}";
        var htmlBody = $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"></head>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                <div style="background: #c0392b; color: white; padding: 20px; border-radius: 8px 8px 0 0;">
                    <h1 style="margin: 0;">Atendee Beauty</h1>
                    <p style="margin: 10px 0 0;">Agendamento Cancelado</p>
                </div>
                <div style="background: #f9f9f9; padding: 20px; border: 1px solid #ddd; border-top: none; border-radius: 0 0 8px 8px;">
                    <p>Olá <strong>{customerName}</strong>,</p>
                    <p>Seu agendamento foi <strong>cancelado</strong>.</p>
                    <table style="width: 100%; border-collapse: collapse; margin: 20px 0;">
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Serviço</td><td style="padding: 10px;">{serviceName}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Profissional</td><td style="padding: 10px;">{professionalName}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Data/Hora original</td><td style="padding: 10px;">{startAtUtc:dd/MM/yyyy HH:mm}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Motivo</td><td style="padding: 10px;">{reason}</td></tr>
                    </table>
                    <p>Para um novo agendamento, entre em contato com o salão.</p>
                    <hr style="border: none; border-top: 1px solid #ddd; margin: 20px 0;">
                    <p style="font-size: 12px; color: #666;">Atendee Beauty</p>
                </div>
            </body>
            </html>
            """;

        var textBody = $"""
            Olá {customerName},

            Seu agendamento foi cancelado.

            Serviço: {serviceName}
            Profissional: {professionalName}
            Data/Hora original: {startAtUtc:dd/MM/yyyy HH:mm}
            Motivo: {reason}

            Para um novo agendamento, entre em contato com o salão.

            Atendee Beauty
            """;

        return SendAsync(to, subject, htmlBody, textBody, cancellationToken);
    }

    public Task<bool> SendAppointmentRescheduleAsync(
        string to,
        string customerName,
        string serviceName,
        string professionalName,
        DateTimeOffset oldStartAtUtc,
        DateTimeOffset newStartAtUtc,
        int durationMinutes,
        string tenantTimeZoneId,
        CancellationToken cancellationToken = default)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(tenantTimeZoneId);
        var localOldStart = TimeZoneInfo.ConvertTime(oldStartAtUtc, zone);
        var localNewStart = TimeZoneInfo.ConvertTime(newStartAtUtc, zone);
        var localNewEnd = localNewStart.AddMinutes(durationMinutes);

        var subject = $"Reagendado: {serviceName} com {professionalName}";
        var htmlBody = $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"></head>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                <div style="background: #e67e22; color: white; padding: 20px; border-radius: 8px 8px 0 0;">
                    <h1 style="margin: 0;">Atendee Beauty</h1>
                    <p style="margin: 10px 0 0;">Agendamento Reagendado</p>
                </div>
                <div style="background: #f9f9f9; padding: 20px; border: 1px solid #ddd; border-top: none; border-radius: 0 0 8px 8px;">
                    <p>Olá <strong>{customerName}</strong>,</p>
                    <p>Seu agendamento foi <strong>reagendado</strong>. O horário original foi mantido no histórico.</p>
                    <table style="width: 100%; border-collapse: collapse; margin: 20px 0;">
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Serviço</td><td style="padding: 10px;">{serviceName}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Profissional</td><td style="padding: 10px;">{professionalName}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Horário anterior</td><td style="padding: 10px;">{localOldStart:dd/MM/yyyy HH:mm}</td></tr>
                        <tr><td style="padding: 10px; background: #fff3cd; font-weight: bold;">Novo horário</td><td style="padding: 10px;">{localNewStart:dd/MM/yyyy HH:mm} - {localNewEnd:HH:mm}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Duração</td><td style="padding: 10px;">{durationMinutes} minutos</td></tr>
                    </table>
                    <p>Se precisar alterar novamente, use o link enviado pelo salão (mínimo 24h de antecedência).</p>
                    <hr style="border: none; border-top: 1px solid #ddd; margin: 20px 0;">
                    <p style="font-size: 12px; color: #666;">Atendee Beauty</p>
                </div>
            </body>
            </html>
            """;

        var textBody = $"""
            Olá {customerName},

            Seu agendamento foi reagendado. O horário original foi mantido no histórico.

            Serviço: {serviceName}
            Profissional: {professionalName}
            Horário anterior: {localOldStart:dd/MM/yyyy HH:mm}
            Novo horário: {localNewStart:dd/MM/yyyy HH:mm} - {localNewEnd:HH:mm}
            Duração: {durationMinutes} minutos

            Se precisar alterar novamente, use o link enviado pelo salão (mínimo 24h de antecedência).

            Atendee Beauty
            """;

        return SendAsync(to, subject, htmlBody, textBody, cancellationToken);
    }

    public Task<bool> SendAppointmentReminderAsync(
        string to,
        string customerName,
        string serviceName,
        string professionalName,
        DateTimeOffset startAtUtc,
        int durationMinutes,
        string tenantTimeZoneId,
        CancellationToken cancellationToken = default)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(tenantTimeZoneId);
        var localStart = TimeZoneInfo.ConvertTime(startAtUtc, zone);
        var localEnd = localStart.AddMinutes(durationMinutes);

        var subject = $"Lembrete: {serviceName} com {professionalName} amanhã";
        var htmlBody = $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"></head>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                <div style="background: #2a5f9c; color: white; padding: 20px; border-radius: 8px 8px 0 0;">
                    <h1 style="margin: 0;">Atendee Beauty</h1>
                    <p style="margin: 10px 0 0;">Lembrete de Agendamento</p>
                </div>
                <div style="background: #f9f9f9; padding: 20px; border: 1px solid #ddd; border-top: none; border-radius: 0 0 8px 8px;">
                    <p>Olá <strong>{customerName}</strong>,</p>
                    <p>Este é um lembrete do seu agendamento para <strong>amanhã</strong>:</p>
                    <table style="width: 100%; border-collapse: collapse; margin: 20px 0;">
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Serviço</td><td style="padding: 10px;">{serviceName}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Profissional</td><td style="padding: 10px;">{professionalName}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Data</td><td style="padding: 10px;">{localStart:dd/MM/yyyy}</td></tr>
                        <tr><td style="padding: 10px; background: #f0f0f0; font-weight: bold;">Horário</td><td style="padding: 10px;">{localStart:HH:mm} - {localEnd:HH:mm}</td></tr>
                    </table>
                    <p>Se precisar cancelar ou reagendar, use o link enviado pelo salão (mínimo 24h de antecedência).</p>
                    <hr style="border: none; border-top: 1px solid #ddd; margin: 20px 0;">
                    <p style="font-size: 12px; color: #666;">Atendee Beauty</p>
                </div>
            </body>
            </html>
            """;

        var textBody = $"""
            Olá {customerName},

            Lembrete: seu agendamento é amanhã!

            Serviço: {serviceName}
            Profissional: {professionalName}
            Data: {localStart:dd/MM/yyyy}
            Horário: {localStart:HH:mm} - {localEnd:HH:mm}
            Duração: {durationMinutes} minutos

            Se precisar cancelar ou reagendar, use o link enviado pelo salão (mínimo 24h de antecedência).

            Atendee Beauty
            """;

        return SendAsync(to, subject, htmlBody, textBody, cancellationToken);
    }
}