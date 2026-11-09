using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

public class UpdateProfileRequest
{
    [StringLength(100, MinimumLength = 1)]
    public string? FullName { get; set; }

    [EmailAddress, StringLength(150)]
    public string? Email { get; set; }

    [Phone, StringLength(20)]
    public string? Phone { get; set; }
}

public class PublicUserResponse
{
    public int UserID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}