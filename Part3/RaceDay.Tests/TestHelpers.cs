using System.Net.Http.Json;
using System.Text.Json;

namespace RaceDay.Tests;

public static class TestHelpers
{
    public const string Password = "Password123";

    public static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, string role, string? organisationName = null) =>
        client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = $"Test {role}",
            email,
            password = Password,
            phone = "0821234567",
            role,
            organisationName
        });

    public static Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    // Registers a user, logs in, and returns a client that carries the session cookie.
    public static async Task<HttpClient> LoggedInClientAsync(RaceDayFactory factory, string email, string role)
    {
        var client = factory.CreateClient();
        (await RegisterAsync(client, email, role)).EnsureSuccessStatusCode();
        (await LoginAsync(client, email)).EnsureSuccessStatusCode();
        return client;
    }

    public static async Task<int> ReadIdAsync(HttpResponseMessage response, string property)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty(property).GetInt32();
    }

    // A date a year ahead, so the tests keep working as time passes.
    public static string FutureDate() => DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");

    // Creates a venue and an event as the given organiser, and returns the event's ID.
    public static async Task<int> CreateEventAsync(HttpClient organiser)
    {
        var venue = await organiser.PostAsJsonAsync("/api/venues", new
        {
            name = "City Hall",
            city = "Pietermaritzburg",
            province = "KwaZulu-Natal"
        });
        venue.EnsureSuccessStatusCode();
        var venueId = await ReadIdAsync(venue, "venueID");

        var ev = await organiser.PostAsJsonAsync("/api/events", new
        {
            name = "Test Marathon",
            description = "A test event",
            eventDate = FutureDate(),
            discipline = "Running",
            venueId,
            distanceKM = 42.2
        });
        ev.EnsureSuccessStatusCode();
        return await ReadIdAsync(ev, "eventID");
    }

    // Adds a category to an event and returns its ID.
    public static async Task<int> CreateCategoryAsync(HttpClient organiser, int eventId)
    {
        var category = await organiser.PostAsJsonAsync($"/api/events/{eventId}/categories", new
        {
            name = "Senior",
            minAge = 20,
            maxAge = 99,
            gender = "Open",
            entryFee = 100
        });
        category.EnsureSuccessStatusCode();
        return await ReadIdAsync(category, "categoryID");
    }
}