namespace RaceDay.Api.Models;

public static class Roles
{
    public const string Participant = "Participant";
    public const string Organiser = "Organiser";
    public const string Admin = "Admin";
}

public class User
{
    public int UserID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.Participant;
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    public ICollection<Organisation> Organisations { get; set; } = new List<Organisation>();
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
}

public class Organisation
{
    public int OrganisationID { get; set; }
    public int ManagedByUserID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    public User ManagedBy { get; set; } = null!;
    public ICollection<Event> Events { get; set; } = new List<Event>();
}

public class Venue
{
    public int VenueID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public ICollection<Event> Events { get; set; } = new List<Event>();
}

public class Event
{
    public int EventID { get; set; }
    public int OrganisationID { get; set; }
    public int VenueID { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }
    public string Discipline { get; set; } = string.Empty; // Running, Cycling, Walking
    public string? Description { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    public Organisation Organisation { get; set; } = null!;
    public Venue Venue { get; set; } = null!;
    public ICollection<RaceRoute> Routes { get; set; } = new List<RaceRoute>();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
}

// Named RaceRoute (not Route) so it never clashes with MVC's [Route] attribute.
// It still maps to the "Routes" table from your ERD.
public class RaceRoute
{
    public int RouteID { get; set; }
    public int EventID { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DistanceKM { get; set; }
    public decimal ElevationGain { get; set; }
    public string? SurfaceType { get; set; }
    public string? GPXFileURL { get; set; }

    public Event Event { get; set; } = null!;
    public ICollection<Category> Categories { get; set; } = new List<Category>();
}

public class Category
{
    public int CategoryID { get; set; }
    public int EventID { get; set; }
    public int RouteID { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DistanceKM { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; } = 99;
    public string Gender { get; set; } = "Open"; // Male, Female, Open
    public decimal EntryFee { get; set; }

    public Event Event { get; set; } = null!;
    public RaceRoute Route { get; set; } = null!;
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
}

public class Registration
{
    public int RegistrationID { get; set; }
    public int UserID { get; set; }
    public int CategoryID { get; set; }
    public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
    public string? BibNumber { get; set; }
    public string PaymentStatus { get; set; } = "Pending"; // Pending, Confirmed, Refunded, Cancelled
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    public User User { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public Result? Result { get; set; }
}

public class Result
{
    public int ResultID { get; set; }
    public int RegistrationID { get; set; }
    public TimeOnly? FinishTime { get; set; }
    public TimeOnly? ChipTime { get; set; }
    public int? OverallPosition { get; set; }
    public int? CategoryPosition { get; set; }
    public string Status { get; set; } = "Finished"; // Finished, DNF, DNS, DQ

    public Registration Registration { get; set; } = null!;
}