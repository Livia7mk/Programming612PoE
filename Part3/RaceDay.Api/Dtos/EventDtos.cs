using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

public class CreateVenueRequest
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(255)]
    public string? Address { get; set; }

    [Required, StringLength(100)]
    public string City { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Province { get; set; } = string.Empty;

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

public class VenueResponse
{
    public int VenueID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

public class CreateEventRequest
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public DateOnly EventDate { get; set; }

    /// <summary>Running, Cycling or Walking.</summary>
    [Required]
    public string Discipline { get; set; } = string.Empty;

    /// <summary>The venue (location) the event is held at.</summary>
    [Required]
    public int VenueId { get; set; }

    /// <summary>Main distance in km. A "Main route" with this distance is created automatically.</summary>
    [Range(0.01, 1000)]
    public decimal DistanceKM { get; set; }
}

public class UpdateEventRequest
{
    [StringLength(150, MinimumLength = 1)]
    public string? Name { get; set; }

    public string? Description { get; set; }
    public DateOnly? EventDate { get; set; }
    public string? Discipline { get; set; }
    public int? VenueId { get; set; }
}

public class RouteSummary
{
    public int RouteID { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DistanceKM { get; set; }
}

public class EventResponse
{
    public int EventID { get; set; }
    public int OrganisationID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly EventDate { get; set; }
    public string Discipline { get; set; } = string.Empty;
    public VenueResponse Venue { get; set; } = null!;
    public List<RouteSummary> Routes { get; set; } = new();
}