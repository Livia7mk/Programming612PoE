using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

[ApiController]
[Authorize] // every endpoint here needs a logged-in user
public class RegistrationsController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public RegistrationsController(RaceDayDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Loads a registration together with the participant, category, event and organisation.
    private IQueryable<Registration> WithDetails() =>
        _db.Registrations
            .Include(r => r.User)
            .Include(r => r.Category).ThenInclude(c => c.Event).ThenInclude(e => e.Organisation);

    /// <summary>Enters the logged-in Participant into an event by choosing a category. Participants only.</summary>
    [HttpPost("api/categories/{categoryId:int}/registrations")]
    [Authorize(Roles = Roles.Participant)]
    [ProducesResponseType(typeof(RegistrationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegistrationResponse>> Enrol(int categoryId, EnrolRequest request)
    {
        var category = await _db.Categories.Include(c => c.Event)
            .FirstOrDefaultAsync(c => c.CategoryID == categoryId);

        if (category is null)
            return NotFound(new { message = "That category does not exist." });

        if (category.Event.EventDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return Conflict(new { message = "This event has already taken place, so entries are closed." });

        // One entry per participant per event, whichever category they pick.
        var alreadyEntered = await _db.Registrations
            .AnyAsync(r => r.UserID == CurrentUserId && r.Category.EventID == category.EventID);
        if (alreadyEntered)
            return Conflict(new { message = "You have already entered this event." });

        var registration = new Registration
        {
            UserID = CurrentUserId,
            CategoryID = categoryId,
            EmergencyContactName = request.EmergencyContactName,
            EmergencyContactPhone = request.EmergencyContactPhone
        };

        try
        {
            _db.Registrations.Add(registration);
            await _db.SaveChangesAsync();

            // The bib number is built from the new registration ID, so it is unique.
            registration.BibNumber = $"B{registration.RegistrationID:D5}";
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "You have already entered this category." });
        }

        var created = await WithDetails().FirstAsync(r => r.RegistrationID == registration.RegistrationID);
        return CreatedAtAction(nameof(GetRegistration),
            new { registrationId = created.RegistrationID }, ToResponse(created));
    }

    /// <summary>Lists the logged-in Participant's own entries.</summary>
    [HttpGet("api/registrations/me")]
    [Authorize(Roles = Roles.Participant)]
    [ProducesResponseType(typeof(List<RegistrationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<RegistrationResponse>>> GetMyRegistrations()
    {
        var registrations = await WithDetails()
            .Where(r => r.UserID == CurrentUserId)
            .OrderBy(r => r.Category.Event.EventDate)
            .ToListAsync();

        return Ok(registrations.Select(ToResponse).ToList());
    }

    /// <summary>Returns one entry. Only the Participant who made it, the Organiser running the event, or an Admin.</summary>
    [HttpGet("api/registrations/{registrationId:int}")]
    [ProducesResponseType(typeof(RegistrationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistrationResponse>> GetRegistration(int registrationId)
    {
        var registration = await WithDetails().FirstOrDefaultAsync(r => r.RegistrationID == registrationId);
        if (registration is null) return NotFound();

        var isOwner = registration.UserID == CurrentUserId;
        var isEventOrganiser = registration.Category.Event.Organisation.ManagedByUserID == CurrentUserId;
        if (!isOwner && !isEventOrganiser && !User.IsInRole(Roles.Admin))
            return Forbid();

        return Ok(ToResponse(registration));
    }

    /// <summary>Updates the emergency contact on the participant's own entry.</summary>
    [HttpPut("api/registrations/{registrationId:int}")]
    [Authorize(Roles = Roles.Participant)]
    [ProducesResponseType(typeof(RegistrationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistrationResponse>> UpdateRegistration(
        int registrationId, UpdateRegistrationRequest request)
    {
        var registration = await WithDetails().FirstOrDefaultAsync(r => r.RegistrationID == registrationId);
        if (registration is null) return NotFound();
        if (registration.UserID != CurrentUserId) return Forbid();

        if (request.EmergencyContactName is not null)
            registration.EmergencyContactName = request.EmergencyContactName;
        if (request.EmergencyContactPhone is not null)
            registration.EmergencyContactPhone = request.EmergencyContactPhone;

        await _db.SaveChangesAsync();
        return Ok(ToResponse(registration));
    }

    /// <summary>Withdraws from an event. Only the Participant who entered (or an Admin).</summary>
    [HttpDelete("api/registrations/{registrationId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Withdraw(int registrationId)
    {
        var registration = await WithDetails().FirstOrDefaultAsync(r => r.RegistrationID == registrationId);
        if (registration is null) return NotFound();

        if (registration.UserID != CurrentUserId && !User.IsInRole(Roles.Admin))
            return Forbid();

        if (registration.Category.Event.EventDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return Conflict(new { message = "This event has already taken place and cannot be withdrawn from." });

        _db.Registrations.Remove(registration);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Pays for an entry. The amount must match the category's entry fee.</summary>
    [HttpPost("api/registrations/{registrationId:int}/payment")]
    [Authorize(Roles = Roles.Participant)]
    [ProducesResponseType(typeof(RegistrationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegistrationResponse>> Pay(int registrationId, PaymentRequest request)
    {
        var registration = await WithDetails().FirstOrDefaultAsync(r => r.RegistrationID == registrationId);
        if (registration is null) return NotFound();
        if (registration.UserID != CurrentUserId) return Forbid();

        if (registration.PaymentStatus == "Confirmed")
            return Conflict(new { message = "This entry has already been paid for." });

        if (request.Amount != registration.Category.EntryFee)
            return BadRequest(new { message = $"The amount must equal the entry fee of {registration.Category.EntryFee}." });

        registration.PaymentStatus = "Confirmed";
        await _db.SaveChangesAsync();
        return Ok(ToResponse(registration));
    }

    /// <summary>Lists everyone entered in an event. Only the Organiser who owns the event (or an Admin).</summary>
    [HttpGet("api/events/{eventId:int}/registrations")]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(typeof(List<RegistrationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<RegistrationResponse>>> GetEventRegistrations(int eventId)
    {
        var ev = await _db.Events.Include(e => e.Organisation)
            .FirstOrDefaultAsync(e => e.EventID == eventId);

        if (ev is null) return NotFound(new { message = "That event does not exist." });

        if (!User.IsInRole(Roles.Admin) && ev.Organisation.ManagedByUserID != CurrentUserId)
            return Forbid();

        var registrations = await WithDetails()
            .Where(r => r.Category.EventID == eventId)
            .OrderBy(r => r.RegistrationDate)
            .ToListAsync();

        return Ok(registrations.Select(ToResponse).ToList());
    }

    private static RegistrationResponse ToResponse(Registration r) => new()
    {
        RegistrationID = r.RegistrationID,
        UserID = r.UserID,
        ParticipantName = r.User.FullName,
        EventID = r.Category.EventID,
        EventName = r.Category.Event.Name,
        EventDate = r.Category.Event.EventDate,
        CategoryID = r.CategoryID,
        CategoryName = r.Category.Name,
        EntryFee = r.Category.EntryFee,
        BibNumber = r.BibNumber,
        PaymentStatus = r.PaymentStatus,
        RegistrationDate = r.RegistrationDate,
        EmergencyContactName = r.EmergencyContactName,
        EmergencyContactPhone = r.EmergencyContactPhone
    };
}