using System.Net.Mail;
using System.Text.Encodings.Web;
using Amazon;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Atendee.Infrastructure;
using Atendee.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

static string Required(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"Required environment variable {name} is not configured.");
    return value.Trim();
}

if (args.Length != 1 ||
    (args[0] is not "create" and not "reissue"))
{
    Console.Error.WriteLine("Usage: Atendee.OperationalIdentityProvisioning <create|reissue>");
    return 64;
}

var mode = args[0];
var connectionString =
    Environment.GetEnvironmentVariable("ConnectionStrings__IdentityProvisioner") ??
    Environment.GetEnvironmentVariable("ATENDEE_IDENTITY_PROVISIONER_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("The dedicated identity-provisioner connection string is required.");

var regionName = Required("AWS_REGION");
var sender = Required("ATENDEE_SES_FROM_EMAIL");
var activationBaseUrl = new Uri(Required("ATENDEE_ACTIVATION_BASE_URL"), UriKind.Absolute);
var configurationSet = Environment.GetEnvironmentVariable("ATENDEE_SES_CONFIGURATION_SET")?.Trim();

if (!string.Equals(activationBaseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
    !string.IsNullOrEmpty(activationBaseUrl.UserInfo) ||
    !string.IsNullOrEmpty(activationBaseUrl.Query) ||
    !string.IsNullOrEmpty(activationBaseUrl.Fragment))
    throw new InvalidOperationException("ATENDEE_ACTIVATION_BASE_URL must be an HTTPS URL without credentials, query or fragment.");

if (!MailAddress.TryCreate(sender, out var senderAddress) ||
    !string.Equals(senderAddress.Address, sender, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("ATENDEE_SES_FROM_EMAIL must be a single valid email address.");

Console.Write("Operational identity email: ");
var email = Console.ReadLine();
if (string.IsNullOrWhiteSpace(email))
    throw new InvalidOperationException("An email address is required.");

var services = new ServiceCollection();
services.AddLogging();
services.AddDataProtection().SetApplicationName("AtendeeBeauty");
services.AddAtendeeIdentity(connectionString);

await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();

var provisioning = scope.ServiceProvider.GetRequiredService<OperationalIdentityProvisioningService>();
var issue = mode == "create"
    ? await provisioning.CreatePendingAsync(email)
    : await provisioning.ReissuePendingAsync(email);

var activationLink = new UriBuilder(activationBaseUrl)
{
    Fragment = "token=" + Uri.EscapeDataString(issue.Token)
}.Uri.AbsoluteUri;

using var ses = new AmazonSimpleEmailServiceV2Client(RegionEndpoint.GetBySystemName(regionName));
var request = new SendEmailRequest
{
    FromEmailAddress = sender,
    Destination = new Destination { ToAddresses = [issue.Email] },
    Content = new EmailContent
    {
        Simple = new Message
        {
            Subject = new Content
            {
                Data = "Activate your Atendee Beauty account",
                Charset = "UTF-8"
            },
            Body = new Body
            {
                Text = new Content
                {
                    Data = "Activate your Atendee Beauty account using this one-time link. " +
                           "The link expires in 30 minutes. If you did not expect this email, ignore it.\n\n" +
                           activationLink,
                    Charset = "UTF-8"
                },
                Html = new Content
                {
                    Data = "<p>Activate your Atendee Beauty account using this one-time link.</p>" +
                           "<p><a href=\"" + HtmlEncoder.Default.Encode(activationLink) +
                           "\">Activate account</a></p>" +
                           "<p>The link expires in 30 minutes. If you did not expect this email, ignore it.</p>",
                    Charset = "UTF-8"
                }
            }
        }
    }
};

if (!string.IsNullOrWhiteSpace(configurationSet))
    request.ConfigurationSetName = configurationSet;

try
{
    var response = await ses.SendEmailAsync(request);
    Console.WriteLine("SES accepted the activation email submission.");
    Console.WriteLine("SES message ID: " + response.MessageId);
    Console.WriteLine("Acceptance is not a delivery confirmation.");
    return 0;
}
catch
{
    Console.Error.WriteLine("Email submission was not confirmed. The raw token was not logged or retained.");
    Console.Error.WriteLine("Run the reissue command to rotate the pending activation token and send a new link.");
    return 3;
}