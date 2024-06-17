using Azure.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using System.Threading.Tasks;
using System;
using BaGetter.Core;

namespace BaGetter.Web.Authentication;

public class NugetBasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptions<BaGetterOptions> bagetterOptions;

    public NugetBasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<BaGetterOptions> bagetterOptions)
        : base(options, logger, encoder)
    {
        this.bagetterOptions = bagetterOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (bagetterOptions.Value.Authentication is null ||
            (
                string.IsNullOrWhiteSpace(bagetterOptions.Value.Authentication.Username) &&
                string.IsNullOrWhiteSpace(bagetterOptions.Value.Authentication.Password))
            )
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Anonymous, string.Empty),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);

            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        else
        {
            if (!Request.Headers.TryGetValue("Authorization", out var auth))
                return Task.FromResult(AuthenticateResult.NoResult());

            string username = null;
            string password = null;
            try
            {
                var authHeader = AuthenticationHeaderValue.Parse(auth);
                var credentialBytes = Convert.FromBase64String(authHeader.Parameter);
                var credentials = Encoding.UTF8.GetString(credentialBytes).Split([':'], 2);
                username = credentials[0];
                password = credentials[1];
            }
            catch
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
            }

            if (!ValidateCredentials(username, password))
                return Task.FromResult(AuthenticateResult.Fail("Invalid Username or Password"));

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, username),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);

            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Basic realm=\"NuGet Server\"";
        await base.HandleChallengeAsync(properties);
    }

    private bool ValidateCredentials(string username, string password)
    {
        return bagetterOptions.Value.Authentication.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && bagetterOptions.Value.Authentication.Password == password;
    }
}
