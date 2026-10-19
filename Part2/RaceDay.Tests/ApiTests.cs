using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;
using Xunit;

namespace RaceDay.Tests;

// Events, categories, enrolments, results and role-based access, all in one place.
public class ApiTests : IDisposable
{
    private readonly RaceDayFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // =====================================================================
    // Helpers
    // =====================================================================

    // An organiser with an event and a category, plus a logged-in participant (not yet entered).
    private async Task<(HttpClient Organiser, HttpClient Participant, int EventId, int CategoryId)> SetUpAsync()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");
        var participant = await TestHelpers.LoggedInClientAsync(_factory, "p@test.com", "Participant");
        var eventId = await TestHelpers.CreateEventAsync(organiser);
        var categoryId = await TestHelpers.CreateCategoryAsync(organiser, eventId);
        return (organiser, participant, eventId, categoryId);
    }

    private static async Task<int> MyUserIdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/users/me");
        response.EnsureSuccessStatusCode();
        return await TestHelpers.ReadIdAsync(response, "userID");
    }

    // Enters the logged-in participant into a category and returns the registration ID.
    private static async Task<int> EnrolAsync(HttpClient participant, int categoryId)
    {
        var response = await participant.PostAsJsonAsync(
            $"/api/categories/{categoryId}/registrations",
            new { emergencyContactName = "Nomsa Dlamini", emergencyContactPhone = "0827771122" });

        response.EnsureSuccessStatusCode();
        return await TestHelpers.ReadIdAsync(response, "registrationID");
    }

    private static async Task<int> CreateVenueAsync(HttpClient organiser)
    {
        var response = await organiser.PostAsJsonAsync("/api/venues", new
        {
            name = "City Hall",
            city = "Pietermaritzburg",
            province = "KwaZulu-Natal"
        });
        response.EnsureSuccessStatusCode();
        return await TestHelpers.ReadIdAsync(response, "venueID");
    }

    private static object NewEvent(int venueId, string discipline = "Running") => new
    {
        name = "Test Marathon",
        description = "A test event",
        eventDate = TestHelpers.FutureDate(),
        discipline,
        venueId,
        distanceKM = 42.2
    };

    private static object FinishedResult(int registrationId) => new
    {
        registrationId,
        finishTime = "01:02:30",
        chipTime = "01:02:10",
        overallPosition = 5,
        categoryPosition = 2,
        status = "Finished"
    };

    // =====================================================================
    // Event management
    // =====================================================================

    [Fact]
    public async Task CreateEvent_AsOrganiser_Returns201()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");
        var venueId = await CreateVenueAsync(organiser);

        var response = await organiser.PostAsJsonAsync("/api/events", NewEvent(venueId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_StoresTheDistanceOnTheMainRoute()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");
        var eventId = await TestHelpers.CreateEventAsync(organiser);

        var json = await (await organiser.GetAsync($"/api/events/{eventId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(42.2m, json.GetProperty("routes")[0].GetProperty("distanceKM").GetDecimal());
    }

    [Fact]
    public async Task UpdateEvent_ByOwner_Returns200_AndSavesTheChange()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");
        var eventId = await TestHelpers.CreateEventAsync(organiser);

        var update = await organiser.PutAsJsonAsync($"/api/events/{eventId}", new { name = "Renamed Marathon" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var json = await (await organiser.GetAsync($"/api/events/{eventId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed Marathon", json.GetProperty("name").GetString());
    }

    [Fact]
    public async Task DeleteEvent_ByOwner_Returns204_AndTheEventIsGone()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");
        var eventId = await TestHelpers.CreateEventAsync(organiser);

        var delete = await organiser.DeleteAsync($"/api/events/{eventId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var afterwards = await organiser.GetAsync($"/api/events/{eventId}");
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
    }

    [Fact]
    public async Task GetEvents_IsPublic_AndListsTheEvent()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");
        await TestHelpers.CreateEventAsync(organiser);

        var response = await _factory.CreateClient().GetAsync("/api/events");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.GetArrayLength());
    }

    [Fact]
    public async Task GetEvent_UnknownId_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_AsParticipant_Returns403()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");
        var venueId = await CreateVenueAsync(organiser);
        var participant = await TestHelpers.LoggedInClientAsync(_factory, "p@test.com", "Participant");

        var response = await participant.PostAsJsonAsync("/api/events", NewEvent(venueId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_WhenNotLoggedIn_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/events", NewEvent(1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_InvalidDiscipline_Returns400()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");
        var venueId = await CreateVenueAsync(organiser);

        var response = await organiser.PostAsJsonAsync("/api/events", NewEvent(venueId, "Swimming"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_UnknownVenue_Returns404()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");

        var response = await organiser.PostAsJsonAsync("/api/events", NewEvent(9999));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_ByAnotherOrganiser_Returns403()
    {
        var owner = await TestHelpers.LoggedInClientAsync(_factory, "owner@test.com", "Organiser");
        var eventId = await TestHelpers.CreateEventAsync(owner);
        var other = await TestHelpers.LoggedInClientAsync(_factory, "other@test.com", "Organiser");

        var response = await other.PutAsJsonAsync($"/api/events/{eventId}", new { name = "Hijacked" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_ByAnotherOrganiser_Returns403()
    {
        var owner = await TestHelpers.LoggedInClientAsync(_factory, "owner@test.com", "Organiser");
        var eventId = await TestHelpers.CreateEventAsync(owner);
        var other = await TestHelpers.LoggedInClientAsync(_factory, "other@test.com", "Organiser");

        var response = await other.DeleteAsync($"/api/events/{eventId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_ThatHasRegistrations_Returns409()
    {
        var (organiser, participant, eventId, categoryId) = await SetUpAsync();
        await EnrolAsync(participant, categoryId);

        var response = await organiser.DeleteAsync($"/api/events/{eventId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // =====================================================================
    // Event enrolments
    // =====================================================================

    [Fact]
    public async Task Enrol_AsParticipant_Returns201()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();

        var response = await participant.PostAsJsonAsync($"/api/categories/{categoryId}/registrations", new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_RecordsTheLinkBetweenParticipantEventAndCategory()
    {
        var (_, participant, eventId, categoryId) = await SetUpAsync();
        var userId = await MyUserIdAsync(participant);

        var response = await participant.PostAsJsonAsync($"/api/categories/{categoryId}/registrations", new { });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // What the API reports back...
        Assert.Equal(userId, json.GetProperty("userID").GetInt32());
        Assert.Equal(eventId, json.GetProperty("eventID").GetInt32());
        Assert.Equal(categoryId, json.GetProperty("categoryID").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("bibNumber").GetString()));
        Assert.Equal("Pending", json.GetProperty("paymentStatus").GetString());

        // ...and what was actually saved in the database.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        Assert.True(await db.Registrations.AnyAsync(r => r.UserID == userId && r.CategoryID == categoryId));
    }

    [Fact]
    public async Task MyRegistrations_ListsTheEntry()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();
        await EnrolAsync(participant, categoryId);

        var response = await participant.GetAsync("/api/registrations/me");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.GetArrayLength());
    }

    [Fact]
    public async Task Organiser_CanSeeEveryoneEnteredInTheirEvent()
    {
        var (organiser, participant, eventId, categoryId) = await SetUpAsync();
        await EnrolAsync(participant, categoryId);

        var response = await organiser.GetAsync($"/api/events/{eventId}/registrations");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.GetArrayLength());
    }

    [Fact]
    public async Task Enrol_Twice_Returns409()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();
        await EnrolAsync(participant, categoryId);

        var second = await participant.PostAsJsonAsync($"/api/categories/{categoryId}/registrations", new { });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Enrol_AsOrganiser_Returns403()
    {
        var (organiser, _, _, categoryId) = await SetUpAsync();

        var response = await organiser.PostAsJsonAsync($"/api/categories/{categoryId}/registrations", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_WhenNotLoggedIn_Returns401()
    {
        var (_, _, _, categoryId) = await SetUpAsync();

        var response = await _factory.CreateClient()
            .PostAsJsonAsync($"/api/categories/{categoryId}/registrations", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_UnknownCategory_Returns404()
    {
        var (_, participant, _, _) = await SetUpAsync();

        var response = await participant.PostAsJsonAsync("/api/categories/9999/registrations", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OtherOrganiser_CannotSeeTheEntries_Returns403()
    {
        var (_, participant, eventId, categoryId) = await SetUpAsync();
        await EnrolAsync(participant, categoryId);
        var other = await TestHelpers.LoggedInClientAsync(_factory, "other@test.com", "Organiser");

        var response = await other.GetAsync($"/api/events/{eventId}/registrations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pay_WithTheWrongAmount_Returns400()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);

        var response = await participant.PostAsJsonAsync(
            $"/api/registrations/{registrationId}/payment", new { paymentMethod = "Card", amount = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pay_WithTheCorrectAmount_ConfirmsTheEntry()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);

        var response = await participant.PostAsJsonAsync(
            $"/api/registrations/{registrationId}/payment", new { paymentMethod = "Card", amount = 100 });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Confirmed", json.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Withdraw_ByTheOwner_Returns204()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);

        var response = await participant.DeleteAsync($"/api/registrations/{registrationId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Withdraw_AnotherParticipantsEntry_Returns403()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);
        var stranger = await TestHelpers.LoggedInClientAsync(_factory, "stranger@test.com", "Participant");

        var response = await stranger.DeleteAsync($"/api/registrations/{registrationId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =====================================================================
    // Role-based access
    // =====================================================================

    [Fact]
    public async Task Participant_CannotCreateAVenue()
    {
        var participant = await TestHelpers.LoggedInClientAsync(_factory, "p@test.com", "Participant");

        var response = await participant.PostAsJsonAsync("/api/venues", new
        {
            name = "Stadium",
            city = "Durban",
            province = "KwaZulu-Natal"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Participant_CannotAddACategory()
    {
        var (_, participant, eventId, _) = await SetUpAsync();

        var response = await participant.PostAsJsonAsync($"/api/events/{eventId}/categories", new { name = "Sneaky" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Participant_CannotLookUpAnotherUser()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");
        var organiserId = await MyUserIdAsync(organiser);
        var participant = await TestHelpers.LoggedInClientAsync(_factory, "p@test.com", "Participant");

        var response = await participant.GetAsync($"/api/users/{organiserId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_CannotViewParticipantEntries()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");

        var response = await organiser.GetAsync("/api/registrations/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_CannotViewParticipantResults()
    {
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");

        var response = await organiser.GetAsync("/api/results/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OtherOrganiser_CannotAddACategoryToSomeoneElsesEvent()
    {
        var owner = await TestHelpers.LoggedInClientAsync(_factory, "owner@test.com", "Organiser");
        var eventId = await TestHelpers.CreateEventAsync(owner);
        var other = await TestHelpers.LoggedInClientAsync(_factory, "other@test.com", "Organiser");

        var response = await other.PostAsJsonAsync($"/api/events/{eventId}/categories", new { name = "Sneaky" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_CanLookUpAUser_ButOnlySeesPublicDetails()
    {
        var participant = await TestHelpers.LoggedInClientAsync(_factory, "p@test.com", "Participant");
        var participantId = await MyUserIdAsync(participant);
        var organiser = await TestHelpers.LoggedInClientAsync(_factory, "org@test.com", "Organiser");

        var response = await organiser.GetAsync($"/api/users/{participantId}");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task LoggedOutUser_CannotListVenues()
    {
        var response = await _factory.CreateClient().GetAsync("/api/venues");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoggedOutUser_CannotViewTheirEntries()
    {
        var response = await _factory.CreateClient().GetAsync("/api/registrations/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AfterLogout_TheSessionNoLongerWorks()
    {
        var client = await TestHelpers.LoggedInClientAsync(_factory, "p@test.com", "Participant");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterwards = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    // =====================================================================
    // Results
    // =====================================================================

    [Fact]
    public async Task Capture_AsTheOwningOrganiser_Returns201()
    {
        var (organiser, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);

        var response = await organiser.PostAsJsonAsync("/api/results", FinishedResult(registrationId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Participant_CanSeeTheirOwnResults()
    {
        var (organiser, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);
        await organiser.PostAsJsonAsync("/api/results", FinishedResult(registrationId));

        var response = await participant.GetAsync("/api/results/me");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.GetArrayLength());
        Assert.Equal(5, json[0].GetProperty("overallPosition").GetInt32());
    }

    [Fact]
    public async Task EventResults_ArePublic()
    {
        var (organiser, participant, eventId, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);
        await organiser.PostAsJsonAsync("/api/results", FinishedResult(registrationId));

        var response = await _factory.CreateClient().GetAsync($"/api/events/{eventId}/results");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.GetArrayLength());
    }

    [Fact]
    public async Task Capture_AsParticipant_Returns403()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);

        var response = await participant.PostAsJsonAsync("/api/results", FinishedResult(registrationId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Capture_ByAnotherOrganiser_Returns403()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);
        var other = await TestHelpers.LoggedInClientAsync(_factory, "other@test.com", "Organiser");

        var response = await other.PostAsJsonAsync("/api/results", FinishedResult(registrationId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Capture_WhenNotLoggedIn_Returns401()
    {
        var (_, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/results", FinishedResult(registrationId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Capture_Twice_Returns409()
    {
        var (organiser, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);
        await organiser.PostAsJsonAsync("/api/results", FinishedResult(registrationId));

        var second = await organiser.PostAsJsonAsync("/api/results", FinishedResult(registrationId));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Capture_FinishedWithoutAFinishTime_Returns400()
    {
        var (organiser, participant, _, categoryId) = await SetUpAsync();
        var registrationId = await EnrolAsync(participant, categoryId);

        var response = await organiser.PostAsJsonAsync("/api/results", new { registrationId, status = "Finished" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Capture_ForAnUnknownRegistration_Returns404()
    {
        var (organiser, _, _, _) = await SetUpAsync();

        var response = await organiser.PostAsJsonAsync("/api/results", FinishedResult(9999));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Participant_CannotSeeAnotherUsersResults_Returns403()
    {
        var (organiser, participant, _, _) = await SetUpAsync();
        var organiserId = await MyUserIdAsync(organiser);

        var response = await participant.GetAsync($"/api/users/{organiserId}/results");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}