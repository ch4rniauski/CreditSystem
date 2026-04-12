using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CreditSystem.Helpers;

public static class ConstraintErrorHandler
{
    private static (string? Message, string? Section) TryParseConstraintError(DbUpdateException ex)
    {
        if (ex.InnerException is not PostgresException pgEx)
        {
            return (null, null);
        }

        if (pgEx.SqlState != "23514" && pgEx.SqlState != "23505" && pgEx.SqlState != "22003") // CHECK, UNIQUE, NUMERIC_VALUE_OUT_OF_RANGE
        {
            return (null, null);
        }

        var constraintName = pgEx.ConstraintName ?? "";

        switch (constraintName)
        {
            // Credit (product) constraint errors
            case "chk_credits_max_amount":
                return ("Сумма мин не может быть больше суммы макс", "product");
            case "chk_credits_max_term":
                return ("Мин срок кредита не может быть больше макс срока", "product");
            case "chk_credits_min_amount":
                return ("Сумма мин должна быть больше 0", "product");
            case "chk_credits_min_term":
                return ("Мин срок кредита должен быть больше 0", "product");
            case "chk_currencies_code_letters_only":
                return ("Код валюты должен содержать только буквы", "currencies");
            // Interest rate constraint errors
            case "chk_interest_rates_term_range":
                return ("Срок 'от' не может быть больше срока 'до'", "interestRates");
            case "chk_interest_rates_date_order":
                return ("Дата начала периода не может быть позже даты окончания", "interestRates");
            case "chk_interest_rates_rate_value_non_negative":
                return ("Ставка не может быть отрицательной", "interestRates");
            case "chk_interest_rates_additive_percent_non_negative":
                return ("Добавка не может быть отрицательной", "interestRates");
            case "chk_interest_rates_term_from":
                return ("Срок 'от' должен быть больше 0", "interestRates");
            case "chk_interest_rates_rate_type":
                return ("Некорректный тип ставки", "interestRates");
            case "chk_interest_rates_rate_rules":
                return ("Некорректная комбинация ставки и добавки", "interestRates");
            case "chk_interest_rates_within_credit_term_bounds":
                return (
                    "Сроки процентной ставки должны быть в пределах минимального и максимального срока кредитного продукта",
                    "interestRates");
            case "chk_interest_rates_no_overlap":
                return (
                    "Эта ставка пересекается с уже существующей. Для одного продукта/валюты не должно быть пересечений по срокам и периоду действия.",
                    "interestRates");
            // Refinance rate constraint errors
            case "chk_refinance_rates_rate":
                return ("Ставка рефинансирования не может быть отрицательной", "refinance");
            case "chk_refinance_rates_date_order":
                return ("Дата начала не может быть позже даты окончания", "refinance");
            case "chk_refinance_rates_no_overlap":
                return ("Период ставки пересекается с существующим периодом", "refinance");
            case "chk_refinance_rates_start_after_existing":
                return (
                    "Дата начала новой ставки должна быть позже дат начала всех уже существующих ставок",
                    "refinance");
        }

        // Credit currency errors (duplicate pair)
        if (pgEx.SqlState == "23505") // UNIQUE constraint violation
        {
            switch (constraintName)
            {
                case "credit_currencies_pkey":
                    return ("Пара валюта-продукт уже существует", "currencies");
                case "refinance_rates_valid_from_date_key":
                    return ("Ставка с такой датой начала уже существует", "refinance");
                case "uq_penalties_credit_type_valid_from":
                    return ("На одну и ту же дату уже существует штраф такого типа", "penalties");
                case "uq_phys_persons_passport":
                    return ("Клиент с такой серией и номером паспорта уже существует", "clients");
                case "uq_phys_persons_phone":
                    return ("Физическое лицо с таким номером телефона уже существует", "clients");
            }
        }

        switch (constraintName)
        {
            // Penalty constraint errors
            case "chk_penalties_value":
                return ("Значение штрафа должно быть от 0 до 100, не включая 100.", "penalties");
            case "chk_penalties_chronological_order":
                return ("Новый штраф должен иметь дату не раньше существующих штрафов того же типа.", "penalties");
            case "chk_payments_applied_annual_rate":
                return ("Примененная процентная ставка в платеже не может быть отрицательной", "payments");
        }

        if (pgEx.SqlState != "22003")
        {
            return (null, null);
        }

        return pgEx.TableName switch
        {
            "contracts" => (
                "Значение процента в договоре слишком большое. Допустимый диапазон: от 0 до 100, не включая 100.",
                "contracts"),
            "interest_rates" => ("Значение ставки слишком большое. Допустимый диапазон: от 0 до 9.9999.",
                "interestRates"),
            "refinance_rates" => ("Значение ставки рефинансирования слишком большое.", "refinance"),
            "penalties" => ("Значение штрафа слишком большое. Допустимый диапазон: от 0 до 100, не включая 100.",
                "penalties"),
            _ => ("Числовое значение выходит за допустимый диапазон.", null)
        };
    }

    public static (string? Message, string? Section) GetConstraintError(DbUpdateException ex)
    {
        var (message, section) = TryParseConstraintError(ex);
        return (message, section);
    }
}
