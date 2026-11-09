using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

[ApiController]
public class ResultsController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public ResultsController(RaceDayDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Loads a result together with its registration, participant, category, event and organisation.
    private IQueryable<Result> WithDetails() =>
        _db.Results
            .Include(r => r.Registration).ThenInclude(g => g.User)
            .Include(r => r.Registration).ThenInclude(g => g.Category)
                .ThenInclude(c => c.Event).ThenInclude(e => e.Organisation);

    /// <summary>Captures a participant's result for an event. Only the Organiser who owns the event (or an Admin).</summary>
    [HttpPost("api/results")]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResultResponse>> CreateResult(CreateResultRequest request)
    {
        var registration = await _db.Registrations
            .Include(r => r.Category).ThenInclude(c => c.Event).ThenInclude(e => e.Organisation)
            .FirstOrDefaultAsync(r => r.RegistrationID == request.RegistrationId);

        if (registration is null)
            return NotFound(new { message = "That registration does not exist." });

        if (!CanManage(registration.Category.Event)) return Forbid();

        var status = NormaliseStatus(request.Status);
        if (status is null)
            return BadRequest(new { message = "Status must be Finished, DNF, DNS or DQ." });

        if (status == "Finished" && request.FinishTime is null)
            return BadRequest(new { message = "A finish time is required when the status is Finished." });

        if (await _db.Results.AnyAsync(r => r.RegistrationID == request.RegistrationId))
            return Conflict(new { message = "A result already exists for this registration." });

        var result = new Result
        {
            RegistrationID = request.RegistrationId,
            FinishTime = request.FinishTime,
            ChipTime = request.ChipTime,
            OverallPosition = request.OverallPosition,
            CategoryPosition = request.CategoryPosition,
            Status = status
        };

        _db.Results.Add(result);
        await _db.SaveChangesAsync();

        var created = await WithDetails().FirstAsync(r => r.ResultID == result.ResultID);
        return CreatedAtAction(nameof(GetResult), new { resultId = created.ResultID }, ToResponse(created));
    }

    /// <summary>Corrects a captured result. Only the Organiser who owns the event (or an Admin).</summary>
    [HttpPut("api/results/{resultId:int}")]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResultResponse>> UpdateResult(int resultId, UpdateResultRequest request)
    {
        var result = await WithDetails().FirstOrDefaultAsync(r => r.ResultID == resultId);
        if (result is null) return NotFound();
        if (!CanManage(result.Registration.Category.Event)) return Forbid();

        if (request.Status is not null)
        {
            var status = NormaliseStatus(request.Status);
            if (status is null)
                return BadRequest(new { message = "Status must be Finished, DNF, DNS or DQ." });
            result.Status = status;
        }

        if (request.FinishTime is not null) result.FinishTime = request.FinishTime;
        if (request.ChipTime is not null) result.ChipTime = request.ChipTime;
        if (request.OverallPosition is not null) result.OverallPosition = request.OverallPosition;
        if (request.CategoryPosition is not null) result.CategoryPosition = request.CategoryPosition;

        if (result.Status == "Finished" && result.FinishTime is null)
            return BadRequest(new { message = "A finish time is required when the status is Finished." });

        await _db.SaveChangesAsync();
        return Ok(ToResponse(result));
    }

    /// <summary>Returns one result. Open to everyone.</summary>
    [HttpGet("api/results/{resultId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResultResponse>> GetResult(int resultId)
    {
        var result = await WithDetails().FirstOrDefaultAsync(r => r.ResultID == resultId);
        if (result is null) return NotFound();

        return Ok(ToResponse(result));
    }

    /// <summary>Lists all results for an event, ordered by overall position. Open to everyone.</summary>
    [HttpGet("api/events/{eventId:int}/results")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<ResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<ResultResponse>>> GetEventResults(int eventId)
    {
        if (!await _db.Events.AnyAsync(e => e.EventID == eventId))
            return NotFound(new { message = "That event does not exist." });

        var results = await WithDetails()
            .Where(r => r.Registration.Category.EventID == eventId)
            .OrderBy(r => r.OverallPosition == null).ThenBy(r => r.OverallPosition)
            .ToListAsync();

        return Ok(results.Select(ToResponse).ToList());
    }

    /// <summary>Lists results for one category, ordered by category position. Open to everyone.</summary>
    [HttpGet("api/categories/{categoryId:int}/results")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<ResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<ResultResponse>>> GetCategoryResults(int categoryId)
    {
        if (!await _db.Categories.AnyAsync(c => c.CategoryID == categoryId))
            return NotFound(new { message = "That category does not exist." });

        var results = await WithDetails()
            .Where(r => r.Registration.CategoryID == categoryId)
            .OrderBy(r => r.CategoryPosition == null).ThenBy(r => r.CategoryPosition)
            .ToListAsync();

        return Ok(results.Select(ToResponse).ToList());
    }

    /// <summary>Lists the logged-in Participant's own results across all events.</summary>
    [HttpGet("api/results/me")]
    [Authorize(Roles = Roles.Participant)]
    [ProducesResponseType(typeof(List<ResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<ResultResponse>>> GetMyResults()
    {
        var results = await WithDetails()
            .Where(r => r.Registration.UserID == CurrentUserId)
            .OrderByDescending(r => r.Registration.Category.Event.EventDate)
            .ToListAsync();

        return Ok(results.Select(ToResponse).ToList());
    }

    /// <summary>Lists one participant's results. Only that participant (or an Admin) can view them.</summary>
    [HttpGet("api/users/{userId:int}/results")]
    [Authorize]
    [ProducesResponseType(typeof(List<ResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<ResultResponse>>> GetUserResults(int userId)
    {
        if (userId != CurrentUserId && !User.IsInRole(Roles.Admin)) return Forbid();

        if (!await _db.Users.AnyAsync(u => u.UserID == userId))
            return NotFound(new { message = "That user does not exist." });

        var results = await WithDetails()
            .Where(r => r.Registration.UserID == userId)
            .OrderByDescending(r => r.Registration.Category.Event.EventDate)
            .ToListAsync();

        return Ok(results.Select(ToResponse).ToList());
    }

    // An Admin can manage any event. An Organiser can only manage events of their own organisation.
    private bool CanManage(Event ev) =>
        User.IsInRole(Roles.Admin) || ev.Organisation.ManagedByUserID == CurrentUserId;

    private static string? NormaliseStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "finished" => "Finished",
            "dnf" => "DNF",
            "dns" => "DNS",
            "dq" => "DQ",
            _ => null
        };

    private static ResultResponse ToResponse(Result r) => new()
    {
        ResultID = r.ResultID,
        RegistrationID = r.RegistrationID,
        ParticipantName = r.Registration.User.FullName,
        BibNumber = r.Registration.BibNumber,
        EventID = r.Registration.Category.EventID,
        EventName = r.Registration.Category.Event.Name,
        CategoryID = r.Registration.CategoryID,
        CategoryName = r.Registration.Category.Name,
        FinishTime = r.FinishTime,
        ChipTime = r.ChipTime,
        OverallPosition = r.OverallPosition,
        CategoryPosition = r.CategoryPosition,
        Status = r.Status
    };
}