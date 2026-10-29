using Bookwell.CustomDomains;
using Microsoft.Extensions.Configuration;

namespace Bookwell.Tests;

public class HostsTests
{
    [Theory]
    [InlineData("Acme.Example.com.", "acme.example.com")]
    [InlineData(" acme.example.com:443 ", "acme.example.com")]
    [InlineData("localhost:3000", "localhost")]
    [InlineData(null, "")]
    public void Normalizes_hosts(string? input, string expected) =>
        Assert.Equal(expected, Hosts.Normalize(input));

    [Theory]
    [InlineData("book.acme.com", true)]
    [InlineData("acme.localhost", true)]
    [InlineData("localhost", false)]
    [InlineData("-acme.com", false)]
    [InlineData("acme..com", false)]
    [InlineData("acme.com/path", false)]
    public void Validates_hosts(string host, bool valid) =>
        Assert.Equal(valid, Hosts.IsValid(host));

    [Fact]
    public void Reserves_the_app_domain_and_app_hosts()
    {
        var options = CustomDomainOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APP_HOSTS"] = "localhost, App.Bookwell.app",
                ["APP_DOMAIN"] = "bookwell.app",
            })
            .Build());

        Assert.True(options.IsReserved("app.bookwell.app"));
        Assert.True(options.IsReserved("bookwell.app"));
        Assert.True(options.IsReserved("admin.bookwell.app"));
        Assert.True(options.IsReserved("localhost"));
        Assert.False(options.IsReserved("book.acme.com"));
        Assert.False(options.IsReserved("notbookwell.app"));
    }

    [Fact]
    public void Unset_tokens_never_match()
    {
        Assert.False(Tokens.Matches("", null));
        Assert.False(Tokens.Matches("", ""));
        Assert.False(Tokens.Matches("secret", "secreT"));
        Assert.False(Tokens.Matches("secret", "secret2"));
        Assert.True(Tokens.Matches("secret", "secret"));
    }
}
