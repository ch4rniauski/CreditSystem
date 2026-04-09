namespace CreditSystem.Database;

public partial class WorkingDay
{
    public int Id { get; set; }

    public DateOnly WorkDate { get; set; }

    public bool IsWorkingDay { get; set; }

    public string DayType { get; set; } = null!;

    public string? HolidayName { get; set; }
}
