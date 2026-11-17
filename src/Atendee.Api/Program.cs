using System.Threading.RateLimiting;
using Atendee.Api.Authentication;
using Atendee.Infrastructure;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Services.AddProblemDetails();
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("bootstrap-health", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("auth-antiforgery", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("auth-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("auth-mfa", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var identityConnectionString = builder.Configuration.GetConnectionString("Identity");
var tenantConnectionString = builder.Configuration.GetConnectionString("Tenant");
var identityEnabled = !string.IsNullOrWhiteSpace(identityConnectionString) &&
                      !string.IsNullOrWhiteSpace(tenantConnectionString);

if (!identityEnabled &&
    (!string.IsNullOrWhiteSpace(identityConnectionString) ||
     !string.IsNullOrWhiteSpace(tenantConnectionString)))
{
    throw new InvalidOperationException(
        "Both ConnectionStrings:Identity and ConnectionStrings:Tenant must be configured together.");
}

if (!identityEnabled && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Operational identity is not configured. Production startup is denied until both database connections and Data Protection are configured.");
}

if (identityEnabled)
{
    var dataProtectionBuilder = builder.Services
        .AddDataProtection()
        .SetApplicationName("AtendeeBeauty");

    var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
    if (!builder.Environment.IsDevelopment())
    {
        if (string.IsNullOrWhiteSpace(keyRingPath))
        {
            throw new InvalidOperationException(
                "DataProtection:KeyRingPath is required outside Development.");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                "Non-Windows production requires an explicitly configured certificate or managed key protector for Data Protection.");
        }

        var keyDirectory = new DirectoryInfo(Path.GetFullPath(keyRingPath));
        keyDirectory.Create();
        dataProtectionBuilder
            .PersistKeysToFileSystem(keyDirectory)
            .ProtectKeysWithDpapi();
    }
    else if (!string.IsNullOrWhiteSpace(keyRingPath))
    {
        var keyDirectory = new DirectoryInfo(Path.GetFullPath(keyRingPath));
        keyDirectory.Create();
        dataProtectionBuilder.PersistKeysToFileSystem(keyDirectory);
    }

    builder.Services.AddAtendeeIdentity(identityConnectionString!);
    builder.Services.AddAtendeePersistence(tenantConnectionString!);

    var authentication = builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });

    authentication.AddIdentityCookies();
    authentication.AddCookie(OperationalAuthenticationEndpoints.MfaSetupScheme, options =>
    {
            options.Cookie.Name = "__Host-Atendee.MfaSetup";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.Name = "__Host-Atendee.Auth";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(
            OperationalAuthenticationEndpoints.MfaSetupPolicy,
            policy =>
            {
                policy.AddAuthenticationSchemes(
                    OperationalAuthenticationEndpoints.MfaSetupScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("atendee:auth_stage", "mfa_setup");
            });
    });

    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-CSRF-TOKEN";
        options.Cookie.Name = "__Host-Atendee.AntiForgery";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });
}

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseRateLimiter();

if (identityEnabled)
{
    app.Use(async (context, next) =>
    {
        var method = context.Request.Method;
        var safeMethod = HttpMethods.IsGet(method) ||
                         HttpMethods.IsHead(method) ||
                         HttpMethods.IsOptions(method) ||
                         HttpMethods.IsTrace(method);
        var isSignedWebhook = context.Request.Path.StartsWithSegments("/api/v1/webhooks");

        if (!safeMethod && !isSignedWebhook)
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "antiforgery_validation_failed" });
                return;
            }
        }

        await next();
    });

    app.UseAuthentication();
    app.UseMiddleware<TenantContextMiddleware>();
    app.UseAuthorization();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }))
   .RequireRateLimiting("bootstrap-health");

if (identityEnabled)
{
    app.MapOperationalAuthentication();
}

app.Run();

// Exposed for integration tests when the test project is added.
public partial class Program { }
