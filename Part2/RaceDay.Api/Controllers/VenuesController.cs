using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/venues")]
public class VenuesController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public VenuesController(RaceDayDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists all venues so an Organiser can pick one for an event.</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(List<VenueResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<VenueResponse>>> GetVenues()
    {
        var venues = await _db.Venues.OrderBy(v => v.Name).ToListAsync();
        return Ok(venues.Select(ToResponse).ToList());
    }

    /// <summary>Creates a venue (event location). Organisers and Admins only.</summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(typeof(VenueResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VenueResponse>> CreateVenue(CreateVenueRequest request)
    {
        var venue = new Venue
        {
            Name = request.Name.Trim(),
            Address = request.Address,
            City = request.City.Trim(),
            Province = request.Province.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude
        };

        _db.Venues.Add(venue);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, ToResponse(venue));
    }

    public static VenueResponse ToResponse(Venue v) => new()
    {
        VenueID = v.VenueID,
        Name = v.Name,
        Address = v.Address,
        City = v.City,
        Province = v.Province,
        Latitude = v.Latitude,
        Longitude = v.Longitude
    };
}