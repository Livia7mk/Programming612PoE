using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;
using Xunit;

namespace RaceDay.Tests;

public class AuthTests : IDisposable
{
    private readonly RaceDayFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // ---------- Registration: success ----------

    [Fact]
    public async Task Register_ValidParticipant_Returns201()
    {
        var client = _factory.CreateClient();

        var response = await TestHelpers.RegisterAsync(client, "p@test.com", "Participant");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Register_ValidOrganiser_CreatesTheirOrganisation()
    {
        var client = _factory.CreateClient();

        var response = await TestHelpers.RegisterAsync(
            client, "o@test.com", "Organiser", "Test Running Club");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        Assert.True(await db.Organisations.AnyAsync(o => o.Name == "Test Running Club"));
    }

    [Fact]
    public async Task Register_StoresHashedPassword_NotPlainText()
    {
        var client = _factory.CreateClient();
        await TestHelpers.RegisterAsync(client, "hash@test.com", "Participant");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == "hash@test.com");

        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
        Assert.NotEqual(TestHelpers.Password, user.PasswordHash);
    }

    // ---------- Registration: failure ----------

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var client = _factory.CreateClient();
        await TestHelpers.RegisterAsync(client, "dup@test.com", "Participant");

        var second = await TestHelpers.RegisterAsync(client, "dup@test.com", "Participant");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_AdminRole_IsRejectedWith400()
    {
        var client = _factory.CreateClient();

        var response = await TestHelpers.RegisterAsync(client, "admin@test.com", "Admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Short Password",
            email = "short@test.com",
            password = "123",
            role = "Participant"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Login: success and failure ----------

    [Fact]
    public async Task Login_CorrectCredentials_Returns200()
    {
        var client = _factory.CreateClient();
        await TestHelpers.RegisterAsync(client, "login@test.com", "Participant");

        var response = await TestHelpers.LoginAsync(client, "login@test.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        await TestHelpers.RegisterAsync(client, "wrong@test.com", "Participant");

        var response = await TestHelpers.LoginAsync(client, "wrong@test.com", "NotThePassword1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await TestHelpers.LoginAsync(client, "nobody@test.com");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Authenticated vs unauthenticated requests ----------

    [Fact]
    public async Task Profile_WhenNotLoggedIn_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_WhenLoggedIn_Returns200()
    {
        var client = await TestHelpers.LoggedInClientAsync(_factory, "me@test.com", "Participant");

        var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}