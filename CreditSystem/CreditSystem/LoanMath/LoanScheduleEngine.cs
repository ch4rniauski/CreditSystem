namespace CreditSystem.LoanMath;

/// <summary>
/// Annuity schedule: daily interest (annual/365) on remaining principal; payment on last weekday (Mon-Fri) of each month.
/// First accrual day = first day of month after issue month.
/// </summary>
public static class LoanScheduleEngine
{
    public const decimal Epsilon = 0.01m;

    public sealed record ScheduleLine(
        int InstallmentIndex,
        DateOnly PlannedPaymentDate,
        decimal ScheduledTotalPayment,
        decimal InterestPortion,
        decimal PrincipalPortion);

    public static DateOnly FirstAccrualDate(DateOnly issueDate)
    {
        var firstOfIssue = new DateOnly(issueDate.Year, issueDate.Month, 1);
        return firstOfIssue.AddMonths(1);
    }

    public static DateOnly PlannedPaymentDateForMonth(DateOnly issueDate, int installmentIndex)
    {
        var accrualStart = FirstAccrualDate(issueDate);
        var monthAnchor = accrualStart.AddMonths(installmentIndex);
        return LastCalendarWeekdayOfMonth(monthAnchor);
    }

    public static DateOnly LastWorkingDayOfMonth(DateOnly anyDayInMonth)
    {
        var y = anyDayInMonth.Year;
        var m = anyDayInMonth.Month;
        var first = new DateOnly(y, m, 1);
        var last = new DateOnly(y, m, DateTime.DaysInMonth(y, m));

        for (var d = last; d >= first; d = d.AddDays(-1))
        {
            if (IsWeekday(d))
            {
                return d;
            }
        }

        return last;
    }

    private static bool IsWeekday(DateOnly d) =>
        d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private static DateOnly LastCalendarWeekdayOfMonth(DateOnly anyDayInMonth)
    {
        var y = anyDayInMonth.Year;
        var m = anyDayInMonth.Month;
        var last = new DateOnly(y, m, DateTime.DaysInMonth(y, m));
        for (var d = last; d >= new DateOnly(y, m, 1); d = d.AddDays(-1))
        {
            if (IsWeekday(d))
                return d;
        }

        return last;
    }

    public static IReadOnlyList<ScheduleLine> BuildSchedule(
        decimal principal,
        decimal annualRate,
        int termMonths,
        DateOnly issueDate)
    {
        if (principal <= 0 || termMonths <= 0)
            return Array.Empty<ScheduleLine>();

        decimal? SimulateEndBalance(decimal pmt)
        {
            var balance = principal;
            DateOnly? prevPayment = null;

            for (var i = 0; i < termMonths; i++)
            {
                var anchor = FirstAccrualDate(issueDate).AddMonths(i);
                var payDate = LastWorkingDayOfMonth(anchor);

                var accrualStart = i == 0
                    ? FirstAccrualDate(issueDate)
                    : prevPayment!.Value.AddDays(1);
                var days = Math.Max(1, payDate.DayNumber - accrualStart.DayNumber + 1);
                var interest = balance * annualRate / 365m * days;
                var principalPart = pmt - interest;
                if (principalPart < 0)
                    return null;
                balance -= principalPart;
                prevPayment = payDate;
            }

            return balance;
        }

        var pmtLow = 0m;
        var pmtHigh = principal / termMonths + principal * annualRate / 12m * 3m + 1m;
        var monthlyPmt = pmtHigh;
        for (var iter = 0; iter < 90; iter++)
        {
            var mid = (pmtLow + pmtHigh) / 2m;
            var end = SimulateEndBalance(mid);
            if (end is null)
            {
                pmtLow = mid;
                continue;
            }

            if (end.Value > Epsilon)
                pmtLow = mid;
            else if (end.Value < -Epsilon)
                pmtHigh = mid;
            else
            {
                monthlyPmt = mid;
                break;
            }

            monthlyPmt = mid;
        }

        monthlyPmt = decimal.Round(monthlyPmt, 2, MidpointRounding.AwayFromZero);

        var lines = new List<ScheduleLine>();
        var bal = principal;
        DateOnly? prev = null;
        for (var i = 0; i < termMonths; i++)
        {
            var anchor = FirstAccrualDate(issueDate).AddMonths(i);
            var payDate = LastWorkingDayOfMonth(anchor);

            var accrualStart = i == 0 ? FirstAccrualDate(issueDate) : prev!.Value.AddDays(1);
            var days = Math.Max(1, payDate.DayNumber - accrualStart.DayNumber + 1);
            var interest = decimal.Round(bal * annualRate / 365m * days, 2, MidpointRounding.AwayFromZero);
            var isLast = i == termMonths - 1;
            var total = isLast ? bal + interest : monthlyPmt;
            var principalPart = total - interest;
            if (principalPart < 0)
                principalPart = 0;
            if (isLast)
            {
                principalPart = bal;
                total = principalPart + interest;
            }

            bal -= principalPart;
            bal = decimal.Round(bal, 2, MidpointRounding.AwayFromZero);
            lines.Add(new ScheduleLine(i, payDate, decimal.Round(total, 2, MidpointRounding.AwayFromZero), interest,
                decimal.Round(principalPart, 2, MidpointRounding.AwayFromZero)));
            prev = payDate;
        }

        return lines;
    }
}
