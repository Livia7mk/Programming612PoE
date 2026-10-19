using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public EventsController(RaceDayDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Lists events, optionally filtered by city, discipline or date range. Open to everyone.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<EventResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<EventResponse>>> GetEvents(
        string? city, string? discipline, DateOnly? dateFrom, DateOnly? dateTo)
    {
        var query = _db.Events.Include(e => e.Venue).Include(e => e.Routes).AsQueryable();

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(e => e.Venue.City == city);

        var normalised = NormaliseDiscipline(discipline);
        if (normalised is not null)
            query = query.Where(e => e.Discipline == normalised);

        if (dateFrom is not null) query = query.Where(e => e.EventDate >= dateFrom);
        if (dateTo is not null) query = query.Where(e => e.EventDate <= dateTo);

        var events = await query.OrderBy(e => e.EventDate).ToListAsync();
        return Ok(events.Select(ToResponse).ToList());
    }

    /// <summary>Returns one event with its venue and routes. Open to everyone.</summary>
    [HttpGet("{eventId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> GetEvent(int eventId)
    {
        var ev = await _db.Events.Include(e => e.Venue).Include(e => e.Routes)
            .FirstOrDefaultAsync(e => e.EventID == eventId);

        if (ev is null) return NotFound();
        return Ok(ToResponse(ev));
    }

    /// <summary>Creates an event under the logged-in Organiser's organisation. Organisers only.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Organiser)]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> CreateEvent(CreateEventRequest request)
    {
        var discipline = NormaliseDiscipline(request.Discipline);
        if (discipline is null)
            return BadRequest(new { message = "Discipline must be Running, Cycling or Walking." });

        var organisation = await _db.Organisations
            .FirstOrDefaultAsync(o => o.ManagedByUserID == CurrentUserId);
        if (organisation is null)
            return BadRequest(new { message = "This organiser has no organisation." });

        var venue = await _db.Venues.FindAsync(request.VenueId);
        if (venue is null)
            return NotFound(new { message = "That venue does not exist." });

        var ev = new Event
        {
            OrganisationID = organisation.OrganisationID,
            VenueID = venue.VenueID,
            Name = request.Name.Trim(),
            Description = request.Description,
            EventDate = request.EventDate,
            Discipline = discipline
        };

        // The brief wants a distance on each event, so a main route is created with it.
        ev.Routes.Add(new RaceRoute { Name = "Main route", DistanceKM = request.DistanceKM });

        _db.Events.Add(ev);
        await _db.SaveChangesAsync();

        ev.Venue = venue;
        return CreatedAtAction(nameof(GetEvent), new { eventId = ev.EventID }, ToResponse(ev));
    }

    /// <summary>Updates an event. Only the Organiser who owns it (or an Admin) can do this.</summary>
    [HttpPut("{eventId:int}")]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> UpdateEvent(int eventId, UpdateEventRequest request)
    {
        var ev = await _db.Events.Include(e => e.Organisation)
            .Include(e => e.Venue).Include(e => e.Routes)
            .FirstOrDefaultAsync(e => e.EventID == eventId);

        if (ev is null) return NotFound();
        if (!CanManage(ev)) return Forbid();

        if (request.Discipline is not null)
        {
            var discipline = NormaliseDiscipline(request.Discipline);
            if (discipline is null)
                return BadRequest(new { message = "Discipline must be Running, Cycling or Walking." });
            ev.Discipline = discipline;
        }

        if (request.VenueId is not null)
        {
            var venue = await _db.Venues.FindAsync(request.VenueId.Value);
            if (venue is null) return NotFound(new { message = "That venue does not exist." });
            ev.VenueID = venue.VenueID;
            ev.Venue = venue;
        }

        if (!string.IsNullOrWhiteSpace(request.Name)) ev.Name = request.Name.Trim();
        if (request.Description is not null) ev.Description = request.Description;
        if (request.EventDate is not null) ev.EventDate = request.EventDate.Value;

        await _db.SaveChangesAsync();
        return Ok(ToResponse(ev));
    }

    /// <summary>Deletes an event that has no registrations. Owner Organiser or Admin only.</summary>
    [HttpDelete("{eventId:int}")]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteEvent(int eventId)
    {
        var ev = await _db.Events.Include(e => e.Organisation)
            .Include(e => e.Routes).Include(e => e.Categories)
            .FirstOrDefaultAsync(e => e.EventID == eventId);

        if (ev is null) return NotFound();
        if (!CanManage(ev)) return Forbid();

        var hasEntries = await _db.Registrations.AnyAsync(r => r.Category.EventID == eventId);
        if (hasEntries)
            return Conflict(new { message = "This event already has registrations and cannot be deleted." });

        _db.Categories.RemoveRange(ev.Categories);
        _db.Routes.RemoveRange(ev.Routes);
        _db.Events.Remove(ev);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // An Admin can manage any event. An Organiser can only manage events of their own organisation.
    private bool CanManage(Event ev) =>
        User.IsInRole(Roles.Admin) || ev.Organisation.ManagedByUserID == CurrentUserId;

    // Accepts "run", "running", "cycle", "cycling", "walk", "walking" in any letter case.
    private static string? NormaliseDiscipline(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "run" or "running" => "Running",
            "cycle" or "cycling" => "Cycling",
            "walk" or "walking" => "Walking",
            _ => null
        };

    private static EventResponse ToResponse(Event e) => new()
    {
        EventID = e.EventID,
        OrganisationID = e.OrganisationID,
        Name = e.Name,
        Description = e.Description,
        EventDate = e.EventDate,
        Discipline = e.Discipline,
        Venue = VenuesController.ToResponse(e.Venue),
        Routes = e.Routes.Select(r => new RouteSummary
        {
            RouteID = r.RouteID,
            Name = r.Name,
            DistanceKM = r.DistanceKM
        }).ToList()
    };
}