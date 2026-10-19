using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

[ApiController]
public class CategoriesController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public CategoriesController(RaceDayDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Lists the categories available for an event. Open to everyone.</summary>
    [HttpGet("api/events/{eventId:int}/categories")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<CategoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<CategoryResponse>>> GetCategoriesForEvent(int eventId)
    {
        if (!await _db.Events.AnyAsync(e => e.EventID == eventId))
            return NotFound(new { message = "That event does not exist." });

        var categories = await _db.Categories
            .Where(c => c.EventID == eventId)
            .OrderBy(c => c.Name)
            .ToListAsync();

        return Ok(categories.Select(ToResponse).ToList());
    }

    /// <summary>Returns one category. Open to everyone.</summary>
    [HttpGet("api/categories/{categoryId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> GetCategory(int categoryId)
    {
        var category = await _db.Categories.FindAsync(categoryId);
        if (category is null) return NotFound();

        return Ok(ToResponse(category));
    }

    /// <summary>Adds a category to an event. Only the Organiser who owns the event (or an Admin) can do this.</summary>
    [HttpPost("api/events/{eventId:int}/categories")]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> CreateCategory(int eventId, CreateCategoryRequest request)
    {
        var ev = await _db.Events
            .Include(e => e.Organisation)
            .Include(e => e.Routes)
            .FirstOrDefaultAsync(e => e.EventID == eventId);

        if (ev is null) return NotFound(new { message = "That event does not exist." });
        if (!CanManage(ev)) return Forbid();

        var gender = NormaliseGender(request.Gender);
        if (gender is null)
            return BadRequest(new { message = "Gender must be Male, Female or Open." });

        if (request.MaxAge < request.MinAge)
            return BadRequest(new { message = "MaxAge cannot be less than MinAge." });

        var name = request.Name.Trim();
        if (await _db.Categories.AnyAsync(c => c.EventID == eventId && c.Name == name))
            return Conflict(new { message = "This event already has a category with that name." });

        // Use the route given, otherwise the event's main (first) route.
        RaceRoute? route = request.RouteId is not null
            ? ev.Routes.FirstOrDefault(r => r.RouteID == request.RouteId)
            : ev.Routes.OrderBy(r => r.RouteID).FirstOrDefault();

        if (route is null)
            return request.RouteId is not null
                ? NotFound(new { message = "That route does not belong to this event." })
                : BadRequest(new { message = "This event has no route yet." });

        var category = new Category
        {
            EventID = eventId,
            RouteID = route.RouteID,
            Name = name,
            DistanceKM = request.DistanceKM ?? route.DistanceKM,
            MinAge = request.MinAge,
            MaxAge = request.MaxAge,
            Gender = gender,
            EntryFee = request.EntryFee
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCategory), new { categoryId = category.CategoryID }, ToResponse(category));
    }

    /// <summary>Updates a category. Only the Organiser who owns the event (or an Admin) can do this.</summary>
    [HttpPut("api/categories/{categoryId:int}")]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> UpdateCategory(int categoryId, UpdateCategoryRequest request)
    {
        var category = await _db.Categories
            .Include(c => c.Event).ThenInclude(e => e.Organisation)
            .FirstOrDefaultAsync(c => c.CategoryID == categoryId);

        if (category is null) return NotFound();
        if (!CanManage(category.Event)) return Forbid();

        if (request.Gender is not null)
        {
            var gender = NormaliseGender(request.Gender);
            if (gender is null)
                return BadRequest(new { message = "Gender must be Male, Female or Open." });
            category.Gender = gender;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
            if (name != category.Name &&
                await _db.Categories.AnyAsync(c => c.EventID == category.EventID && c.Name == name))
                return Conflict(new { message = "This event already has a category with that name." });

            category.Name = name;
        }

        if (request.RouteId is not null)
        {
            var route = await _db.Routes
                .FirstOrDefaultAsync(r => r.RouteID == request.RouteId && r.EventID == category.EventID);
            if (route is null)
                return NotFound(new { message = "That route does not belong to this event." });

            category.RouteID = route.RouteID;
        }

        if (request.DistanceKM is not null) category.DistanceKM = request.DistanceKM.Value;
        if (request.MinAge is not null) category.MinAge = request.MinAge.Value;
        if (request.MaxAge is not null) category.MaxAge = request.MaxAge.Value;
        if (request.EntryFee is not null) category.EntryFee = request.EntryFee.Value;

        if (category.MaxAge < category.MinAge)
            return BadRequest(new { message = "MaxAge cannot be less than MinAge." });

        await _db.SaveChangesAsync();
        return Ok(ToResponse(category));
    }

    /// <summary>Deletes a category that nobody has entered yet. Owner Organiser or Admin only.</summary>
    [HttpDelete("api/categories/{categoryId:int}")]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(int categoryId)
    {
        var category = await _db.Categories
            .Include(c => c.Event).ThenInclude(e => e.Organisation)
            .FirstOrDefaultAsync(c => c.CategoryID == categoryId);

        if (category is null) return NotFound();
        if (!CanManage(category.Event)) return Forbid();

        if (await _db.Registrations.AnyAsync(r => r.CategoryID == categoryId))
            return Conflict(new { message = "This category already has registrations and cannot be deleted." });

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // An Admin can manage any event. An Organiser can only manage events of their own organisation.
    private bool CanManage(Event ev) =>
        User.IsInRole(Roles.Admin) || ev.Organisation.ManagedByUserID == CurrentUserId;

    private static string? NormaliseGender(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "male" or "m" => "Male",
            "female" or "f" => "Female",
            "open" or "" or null => "Open",
            _ => null
        };

    private static CategoryResponse ToResponse(Category c) => new()
    {
        CategoryID = c.CategoryID,
        EventID = c.EventID,
        RouteID = c.RouteID,
        Name = c.Name,
        DistanceKM = c.DistanceKM,
        MinAge = c.MinAge,
        MaxAge = c.MaxAge,
        Gender = c.Gender,
        EntryFee = c.EntryFee
    };
}