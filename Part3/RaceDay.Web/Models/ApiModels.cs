namespace RaceDay.Web.Models;

// Mirrors the user object returned by the Part 2 API.
public class UserDto
{
    public int UserID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
}