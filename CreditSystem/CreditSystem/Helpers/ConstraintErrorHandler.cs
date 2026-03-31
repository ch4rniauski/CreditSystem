using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CreditSystem.Controllers;

/// <summary>
/// Handles PostgreSQL constraint violation errors and maps them to user-friendly messages.
/// </summary>
public static class ConstraintErrorHandler
{
    /// <summary>
    /// Extracts and translates constraint error message from DbUpdateException.
    /// </summary>
    public static (bool IsConstraintError, string? Message, string? Section) TryParseConstraintError(DbUpdateException ex)
    {
        if (ex.InnerException is not PostgresException pgEx)
            return (false, null, null);

        if (pgEx.SqlState != "23514" && pgEx.SqlState != "23505") // CHECK and UNIQUE constraint codes
            return (false, null, null);

        var constraintName = pgEx.ConstraintName ?? "";

        // Credit (product) constraint errors
        if (constraintName == "chk_credits_max_amount")
            return (true, "Сумма мин не может быть больше суммы макс", "product");
        if (constraintName == "chk_credits_max_term")
            return (true, "Мин срок кредита не может быть больше макс срока", "product");
        if (constraintName == "chk_credits_min_amount")
            return (true, "Сумма мин должна быть больше 0", "product");
        if (constraintName == "chk_credits_min_term")
            return (true, "Мин срок кредита должен быть больше 0", "product");

        // Interest rate constraint errors
        if (constraintName == "chk_interest_rates_term_range")
            return (true, "Срок 'от' не может быть больше срока 'до'", "interestRates");
        if (constraintName == "chk_interest_rates_date_order")
            return (true, "Дата начала периода не может быть позже даты окончания", "interestRates");
        if (constraintName == "chk_interest_rates_rate_value_non_negative")
            return (true, "Ставка не может быть отрицательной", "interestRates");
        if (constraintName == "chk_interest_rates_additive_percent_non_negative")
            return (true, "Добавка не может быть отрицательной", "interestRates");
        if (constraintName == "chk_interest_rates_term_from")
            return (true, "Срок 'от' должен быть больше 0", "interestRates");
        if (constraintName == "chk_interest_rates_rate_type")
            return (true, "Некорректный тип ставки", "interestRates");
        if (constraintName == "chk_interest_rates_rate_rules")
            return (true, "Некорректная комбинация ставки и добавки", "interestRates");

        // Refinance rate constraint errors
        if (constraintName == "chk_refinance_rates_rate")
            return (true, "Ставка рефинансирования не может быть отрицательной", "refinance");

        // Credit currency errors (duplicate pair)
        if (pgEx.SqlState == "23505") // UNIQUE constraint violation
        {
            if (constraintName == "credit_currencies_pkey")
                return (true, "Пара валюта-продукт уже существует", "currencies");
        }

        // Penalty constraint errors
        if (constraintName == "chk_penalties_value")
            return (true, "Штраф не может быть отрицательным", "penalties");

        return (false, null, null);
    }

    /// <summary>
    /// Gets the error message from a constraint violation.
    /// </summary>
    public static (string? Message, string? Section) GetConstraintError(DbUpdateException ex)
    {
        var (isConstraint, message, section) = TryParseConstraintError(ex);
        return (message, section);
    }
}
