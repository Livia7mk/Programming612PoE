using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

public class EnrolRequest
{
    [StringLength(100)]
    public string? EmergencyContactName { get; set; }

    [Phone, StringLength(20)]
    public string? EmergencyContactPhone { get; set; }
}

public class UpdateRegistrationRequest
{
    [StringLength(100)]
    public string? EmergencyContactName { get; set; }

    [Phone, StringLength(20)]
    public string? EmergencyContactPhone { get; set; }
}

public class PaymentRequest
{
    /// <summary>For example "Card" or "EFT".</summary>
    [Required, StringLength(30)]
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>Must equal the category's entry fee.</summary>
    [Required]
    public decimal Amount { get; set; }

    [StringLength(100)]
    public string? TransactionRef { get; set; }
}

public class RegistrationResponse
{
    public int RegistrationID { get; set; }
    public int UserID { get; set; }
    public string ParticipantName { get; set; } = string.Empty;
    public int EventID { get; set; }
    public string EventName { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal EntryFee { get; set; }
    public string? BibNumber { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
}