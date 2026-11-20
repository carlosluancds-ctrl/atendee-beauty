using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Atendee.Api.Authentication;
using Atendee.Api.Customers;
using Atendee.Api.Scheduling;
using Atendee.Api.Security;
using Atendee.Api.Services;
using Atendee.Api.Tenants;
using Atendee.Api.Professionals;
using Atendee.Infrastructure;
using Atendee.Infrastructure.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    ApiRequestLimits.Apply(options.Limits);
});
builder.Services.AddProblemDetails();
builder.Services.AddAuthorization();

var configuredTrustedProxyAddresses =
    builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
var trustedProxyAddresses = new List<IPAddress>(configuredTrustedProxyAddresses.Length);
foreach (var configuredAddress in configuredTrustedProxyAddresses)
{
    if (!IPAddress.TryParse(configuredAddress, out var address))
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:KnownProxies must contain only exact, valid unicast IP addresses.");
    }

    // Normalize IPv4-mapped IPv6 values before validation and storage so
    // unspecified, multicast and reserved IPv4 ranges cannot bypass checks.
    var canonicalAddress = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    var isReservedOrMulticastIpv4 =
        canonicalAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
        canonicalAddress.GetAddressBytes()[0] >= 224;

    if (canonicalAddress.Equals(IPAddress.Any) ||
        canonicalAddress.Equals(IPAddress.IPv6Any) ||
        canonicalAddress.Equals(IPAddress.Broadcast) ||
        canonicalAddress.IsIPv6Multicast ||
        isReservedOrMulticastIpv4)
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:KnownProxies must contain only exact, valid unicast IP addresses.");
    }

    trustedProxyAddresses.Add(canonicalAddress);
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.RequireHeaderSymmetry = true;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    if (trustedProxyAddresses.Count == 0)
    {
        // An empty known-proxy collection disables the middleware's address
        // check in ASP.NET Core. Keep the check enabled with unmatchable
        // unspecified addresses so the default policy trusts no real proxy.
        options.KnownProxies.Add(IPAddress.Any);
        options.KnownProxies.Add(IPAddress.IPv6Any);
    }
    else
    {
        foreach (var trustedProxyAddress in trustedProxyAddresses)
        {
            options.KnownProxies.Add(trustedProxyAddress);
        }
    }
});

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
    var partitionKeySecretSetting = builder.Configuration["AuthenticationRateLimiting:PartitionKeySecret"];
    byte[] authenticationRateLimitPartitionSecret;
    try
    {
        authenticationRateLimitPartitionSecret = Convert.FromBase64String(
            partitionKeySecretSetting ?? string.Empty);
    }
    catch (FormatException exception)
    {
        throw new InvalidOperationException(
            "AuthenticationRateLimiting:PartitionKeySecret must be a base64-encoded secret containing at least 32 bytes.",
            exception);
    }

    if (authenticationRateLimitPartitionSecret.Length < 32)
    {
        throw new InvalidOperationException(
            "AuthenticationRateLimiting:PartitionKeySecret must be a base64-encoded secret containing at least 32 bytes.");
    }

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

        var keyDirectory = new DirectoryInfo(Path.GetFullPath(keyRingPath));
        keyDirectory.Create();

        var certificatePath = builder.Configuration["DataProtection:CertificatePath"];
        var certificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            if (string.IsNullOrWhiteSpace(certificatePassword))
            {
                throw new InvalidOperationException(
                    "DataProtection:CertificatePassword is required when a certificate protector is configured.");
            }

            X509Certificate2 certificate;
            try
            {
                certificate = X509CertificateLoader.LoadPkcs12FromFile(
                    Path.GetFullPath(certificatePath),
                    certificatePassword,
                    X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (CryptographicException exception)
            {
                throw new InvalidOperationException(
                    "The configured Data Protection certificate could not be loaded.", exception);
            }

            using var publicKey = certificate.GetRSAPublicKey();
            using var privateKey = certificate.GetRSAPrivateKey();
            if (!certificate.HasPrivateKey ||
                certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow ||
                certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow ||
                publicKey is null ||
                privateKey is null)
            {
                certificate.Dispose();
                throw new InvalidOperationException(
                    "The Data Protection certificate must be a currently valid RSA certificate with its private key.");
            }

            builder.Services.AddSingleton(certificate);
            dataProtectionBuilder
                .PersistKeysToFileSystem(keyDirectory)
                .ProtectKeysWithCertificate(certificate);
        }
        else if (OperatingSystem.IsWindows())
        {
            dataProtectionBuilder
                .PersistKeysToFileSystem(keyDirectory)
                .ProtectKeysWithDpapi();
        }
        else
        {
            throw new InvalidOperationException(
                "Non-Windows production requires DataProtection:CertificatePath and DataProtection:CertificatePassword.");
        }
    }
    else if (!string.IsNullOrWhiteSpace(keyRingPath))
    {
        var keyDirectory = new DirectoryInfo(Path.GetFullPath(keyRingPath));
        keyDirectory.Create();
        dataProtectionBuilder.PersistKeysToFileSystem(keyDirectory);
    }

    builder.Services.AddAtendeeIdentity(identityConnectionString!, authenticationRateLimitPartitionSecret);
    builder.Services.AddAtendeePersistence(tenantConnectionString!);
    builder.Services.AddAtendeeDatabaseReadiness(tenantConnectionString!);

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
    authentication.AddCookie(OperationalAuthenticationEndpoints.MfaChallengeScheme, options =>
    {
        options.Cookie.Name = "__Host-Atendee.MfaChallenge";
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

    // Revalidate the Identity security stamp on every authenticated request.
    // Account disablement and session revocation therefore take effect on the
    // next request instead of waiting for the default 30-minute interval.
    builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    {
        options.ValidationInterval = TimeSpan.Zero;
        options.OnRefreshingPrincipal = context =>
        {
            // Security-stamp refresh rebuilds the principal from persisted user
            // claims. Preserve only the signed, session-scoped authentication
            // method markers so an MFA-authenticated Owner/Manager does not
            // lose amr=mfa on the first request after a refresh.
            var refreshedIdentity = context.NewPrincipal?.Identities
                .FirstOrDefault(identity => identity.IsAuthenticated);
            if (refreshedIdentity is not null && context.CurrentPrincipal is not null)
            {
                foreach (var authenticationMethod in context.CurrentPrincipal
                    .FindAll("amr")
                    .Select(claim => claim.Value)
                    .Where(value => value is "pwd" or "mfa")
                    .Distinct(StringComparer.Ordinal))
                {
                    if (!refreshedIdentity.HasClaim("amr", authenticationMethod))
                    {
                        refreshedIdentity.AddClaim(new System.Security.Claims.Claim(
                            "amr", authenticationMethod));
                    }
                }
            }

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

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // V91: the API only returns JSON/empty bodies; forbid any active content, framing,
    // cross-origin embedding and cross-origin window references by default.
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
// V92: bounded request bodies, rejected before routing, authentication and antiforgery.
app.UseMiddleware<RequestBodySizeLimitMiddleware>();
app.UseExceptionHandler();
app.UseRouting();
// Shared auth limits run after trusted proxy resolution/routing and before authentication/antiforgery.
app.UseMiddleware<DistributedAuthenticationRateLimitingMiddleware>();
app.UseRateLimiter();

if (identityEnabled)
{
    // Authentication must establish the current claims principal before
    // antiforgery validates a request token bound to that principal.
    app.UseAuthentication();

    app.Use(async (context, next) =>
    {
        var method = context.Request.Method;
        var safeMethod = HttpMethods.IsGet(method) ||
                         HttpMethods.IsHead(method) ||
                         HttpMethods.IsOptions(method) ||
                         HttpMethods.IsTrace(method);
        // Fail closed for every unsafe request. Webhook endpoints are not
        // implemented yet; when introduced, they must opt out only through
        // endpoint-specific signature-validation metadata, never a path prefix.

        if (!safeMethod)
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException exception)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Atendee.Api.Antiforgery");
                logger.LogWarning(
                    "Antiforgery validation rejected {Method} {Path}. RequestTokenPresent={RequestTokenPresent}, CookieTokenPresent={CookieTokenPresent}, Reason={Reason}",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Request.Headers.ContainsKey("X-CSRF-TOKEN"),
                    context.Request.Cookies.ContainsKey("__Host-Atendee.AntiForgery"),
                    exception.Message);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "antiforgery_validation_failed" });
                return;
            }
        }

        await next();
    });

    app.UseMiddleware<TenantContextMiddleware>();
    app.UseAuthorization();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }))
   .RequireRateLimiting("bootstrap-health");

// Readiness: anonymous like /health/live, but reports 503 when PostgreSQL is
// unreachable. The body is intentionally minimal; no dependency details leak.
app.MapGet("/health/ready", async (HttpContext context, CancellationToken cancellationToken) =>
    {
        context.Response.Headers.CacheControl = "no-store";

        // Without configured databases (Development only) there is nothing to probe.
        var probe = context.RequestServices.GetService<IDatabaseReadinessProbe>();
        if (probe is null || await probe.IsReadyAsync(cancellationToken))
        {
            return Results.Ok(new { status = "ready" });
        }

        return Results.Json(
            new { status = "unavailable" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    })
   .RequireRateLimiting("bootstrap-health");

if (identityEnabled)
{
    app.MapOperationalAuthentication();
    app.MapTenantServices();
    app.MapTenantCustomers();
    app.MapTenantBusinessHours();
    app.MapTenantOnboarding();
    app.MapTenantProfessionals();
    app.MapTenantProfessionalAvailability();
    app.MapTenantProfessionalTimeOff();
    app.MapTenantAvailability();
    app.MapTenantAppointments();
}

app.Run();

// Exposed for integration tests when the test project is added.
public partial class Program { }
