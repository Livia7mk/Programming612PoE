using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

public class CreateResultRequest
{
    [Required]
    public int RegistrationId { get; set; }

    /// <summary>Gun time, for example "01:02:30". Required when Status is Finished.</summary>
    public TimeOnly? FinishTime { get; set; }

    /// <summary>Chip time, for example "01:02:10".</summary>
    public TimeOnly? ChipTime { get; set; }

    [Range(1, int.MaxValue)]
    public int? OverallPosition { get; set; }

    [Range(1, int.MaxValue)]
    public int? CategoryPosition { get; set; }

    /// <summary>Finished, DNF, DNS or DQ.</summary>
    public string Status { get; set; } = "Finished";
}

public class UpdateResultRequest
{
    public TimeOnly? FinishTime { get; set; }
    public TimeOnly? ChipTime { get; set; }

    [Range(1, int.MaxValue)]
    public int? OverallPosition { get; set; }

    [Range(1, int.MaxValue)]
    public int? CategoryPosition { get; set; }

    public string? Status { get; set; }
}

public class ResultResponse
{
    public int ResultID { get; set; }
    public int RegistrationID { get; set; }
    public string ParticipantName { get; set; } = string.Empty;
    public string? BibNumber { get; set; }
    public int EventID { get; set; }
    public string EventName { get; set; } = string.Empty;
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public TimeOnly? FinishTime { get; set; }
    public TimeOnly? ChipTime { get; set; }
    public int? OverallPosition { get; set; }
    public int? CategoryPosition { get; set; }
    public string Status { get; set; } = string.Empty;
}