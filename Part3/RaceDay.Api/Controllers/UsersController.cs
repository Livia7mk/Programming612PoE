using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize] // every endpoint here needs a logged-in user
public class UsersController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public UsersController(RaceDayDbContext db)
    {
        _db = db;
    }

    // The logged-in user's ID, read from the session cookie.
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Returns the logged-in user's own profile.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> GetMyProfile()
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();

        return Ok(ToResponse(user));
    }

    /// <summary>Updates the logged-in user's own name, email or phone number.</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> UpdateMyProfile(UpdateProfileRequest request)
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim().ToLowerInvariant();

            // Reject the change if another account already uses this email.
            if (email != user.Email && await _db.Users.AnyAsync(u => u.Email == email))
                return Conflict(new { message = "That email is already in use." });

            user.Email = email;
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName.Trim();

        if (request.Phone is not null)
            user.Phone = request.Phone;

        await _db.SaveChangesAsync();
        return Ok(ToResponse(user));
    }

    /// <summary>Lets an Organiser or Admin look up another user's public details.</summary>
    [HttpGet("{userId:int}")]
    [Authorize(Roles = $"{Roles.Organiser},{Roles.Admin}")]
    [ProducesResponseType(typeof(PublicUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicUserResponse>> GetUser(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        return Ok(new PublicUserResponse
        {
            UserID = user.UserID,
            FullName = user.FullName,
            Role = user.Role
        });
    }

    private static UserResponse ToResponse(User u) => new()
    {
        UserID = u.UserID,
        FullName = u.FullName,
        Email = u.Email,
        Phone = u.Phone,
        Role = u.Role
    };
}