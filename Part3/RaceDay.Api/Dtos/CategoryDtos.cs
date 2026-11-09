using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

public class CreateCategoryRequest
{
    /// <summary>For example "Under 20", "Senior", "10km" or "21km".</summary>
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Distance in km. If left out, the route's distance is used.</summary>
    [Range(0.01, 1000)]
    public decimal? DistanceKM { get; set; }

    [Range(0, 120)]
    public int MinAge { get; set; } = 0;

    [Range(0, 120)]
    public int MaxAge { get; set; } = 99;

    /// <summary>Male, Female or Open.</summary>
    public string Gender { get; set; } = "Open";

    [Range(0, 100000)]
    public decimal EntryFee { get; set; } = 0;

    /// <summary>Optional. If left out, the event's main route is used.</summary>
    public int? RouteId { get; set; }
}

public class UpdateCategoryRequest
{
    [StringLength(100, MinimumLength = 1)]
    public string? Name { get; set; }

    [Range(0.01, 1000)]
    public decimal? DistanceKM { get; set; }

    [Range(0, 120)]
    public int? MinAge { get; set; }

    [Range(0, 120)]
    public int? MaxAge { get; set; }

    public string? Gender { get; set; }

    [Range(0, 100000)]
    public decimal? EntryFee { get; set; }

    public int? RouteId { get; set; }
}

public class CategoryResponse
{
    public int CategoryID { get; set; }
    public int EventID { get; set; }
    public int RouteID { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DistanceKM { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; }
    public string Gender { get; set; } = string.Empty;
    public decimal EntryFee { get; set; }
}