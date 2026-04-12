-- Пункт 9: календарь платежей (дата планового платежа = последний рабочий день месяца).
-- Условие: требуется определять рабочие дни для расчета плановых дат.
CREATE OR REPLACE FUNCTION is_weekday(p_date DATE)
RETURNS BOOLEAN
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT EXTRACT(ISODOW FROM p_date) BETWEEN 1 AND 5;
$$;

-- Пункт 9: календарь платежей (последний рабочий день месяца).
-- Условие: плановая дата каждого ежемесячного платежа должна попадать на последний рабочий день месяца.
CREATE OR REPLACE FUNCTION last_workday_of_month(p_any_day_in_month DATE)
RETURNS DATE
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT MAX(d)::DATE
    FROM generate_series(
        date_trunc('month', p_any_day_in_month)::DATE,
        (date_trunc('month', p_any_day_in_month) + interval '1 month - 1 day')::DATE,
        interval '1 day') AS gs(d)
    WHERE is_weekday(gs.d::DATE);
$$;

-- Пункты 7 и 9: ожидаемые платежи и календарь платежей.
-- Условие: начисление процентов начинается с первого дня месяца, следующего за датой договора.
CREATE OR REPLACE FUNCTION first_accrual_date(p_issue_date DATE)
RETURNS DATE
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT (date_trunc('month', p_issue_date) + interval '1 month')::DATE;
$$;

-- Пункты 7 и 9: ожидаемые платежи и календарь платежей.
-- Условие: требуется вычислять плановую дату платежа по номеру взноса.
CREATE OR REPLACE FUNCTION planned_payment_date_for_month(p_issue_date DATE, p_installment_index INT)
RETURNS DATE
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT last_workday_of_month((first_accrual_date(p_issue_date) + make_interval(months => p_installment_index))::DATE);
$$;

-- Пункты 4, 6, 7, 8 и 9: активные клиенты, договоры с малым остатком, ожидаемые платежи, текущий долг, календарь.
-- Условие: нужен единый расчет аннуитетного графика с разбивкой на проценты и основной долг.
CREATE OR REPLACE FUNCTION build_schedule(
    p_principal NUMERIC,
    p_annual_rate_fraction NUMERIC,
    p_term_months INT,
    p_issue_date DATE)
RETURNS TABLE (
    installment_number INT,
    planned_date DATE,
    expected_payment NUMERIC,
    expected_interest NUMERIC,
    expected_principal NUMERIC
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_epsilon CONSTANT NUMERIC := 0.01;
    v_low NUMERIC := 0;
    v_high NUMERIC;
    v_mid NUMERIC;
    v_monthly_payment NUMERIC;
    v_iter INT;
    v_balance NUMERIC;
    v_prev_payment DATE;
    v_pay_date DATE;
    v_accrual_start DATE;
    v_days INT;
    v_interest NUMERIC;
    v_principal_part NUMERIC;
    v_is_bad BOOLEAN;
    v_i INT;
    v_is_last BOOLEAN;
    v_total NUMERIC;
BEGIN
    IF p_principal <= 0 OR p_term_months <= 0 THEN
        RETURN;
    END IF;

    v_high := p_principal / p_term_months + p_principal * p_annual_rate_fraction / 12 * 3 + 1;
    v_monthly_payment := v_high;

    FOR v_iter IN 1..90 LOOP
        v_mid := (v_low + v_high) / 2;
        v_balance := p_principal;
        v_prev_payment := NULL;
        v_is_bad := FALSE;

        FOR v_i IN 0..(p_term_months - 1) LOOP
            v_pay_date := planned_payment_date_for_month(p_issue_date, v_i);
            IF v_i = 0 THEN
                v_accrual_start := first_accrual_date(p_issue_date);
            ELSE
                v_accrual_start := v_prev_payment + 1;
            END IF;

            v_days := GREATEST(1, v_pay_date - v_accrual_start + 1);
            v_interest := v_balance * p_annual_rate_fraction / 365 * v_days;
            v_principal_part := v_mid - v_interest;

            IF v_principal_part < 0 THEN
                v_is_bad := TRUE;
                EXIT;
            END IF;

            v_balance := v_balance - v_principal_part;
            v_prev_payment := v_pay_date;
        END LOOP;

        IF v_is_bad THEN
            v_low := v_mid;
            CONTINUE;
        END IF;

        IF v_balance > v_epsilon THEN
            v_low := v_mid;
        ELSIF v_balance < -v_epsilon THEN
            v_high := v_mid;
        ELSE
            v_monthly_payment := v_mid;
            EXIT;
        END IF;

        v_monthly_payment := v_mid;
    END LOOP;

    v_monthly_payment := ROUND(v_monthly_payment, 2);

    v_balance := p_principal;
    v_prev_payment := NULL;

    FOR v_i IN 0..(p_term_months - 1) LOOP
        v_pay_date := planned_payment_date_for_month(p_issue_date, v_i);

        IF v_i = 0 THEN
            v_accrual_start := first_accrual_date(p_issue_date);
        ELSE
            v_accrual_start := v_prev_payment + 1;
        END IF;

        v_days := GREATEST(1, v_pay_date - v_accrual_start + 1);
        v_interest := ROUND(v_balance * p_annual_rate_fraction / 365 * v_days, 2);

        v_is_last := v_i = p_term_months - 1;
        v_total := CASE
            WHEN v_is_last THEN v_balance + v_interest
            ELSE v_monthly_payment
        END;

        v_principal_part := v_total - v_interest;
        IF v_principal_part < 0 THEN
            v_principal_part := 0;
        END IF;

        IF v_is_last THEN
            v_principal_part := v_balance;
            v_total := v_principal_part + v_interest;
        END IF;

        v_balance := ROUND(v_balance - v_principal_part, 2);

        installment_number := v_i + 1;
        planned_date := v_pay_date;
        expected_payment := ROUND(v_total, 2);
        expected_interest := v_interest;
        expected_principal := ROUND(v_principal_part, 2);
        RETURN NEXT;

        v_prev_payment := v_pay_date;
    END LOOP;
END;
$$;

-- Пункт 1: перечень и общее число договоров по разным разрезам и периоду оформления.
-- Условие: группировка по статусу/типу клиента/валюте/типу ставки/диапазонам сумм и сроков.
CREATE OR REPLACE FUNCTION report_contract_distribution(
    p_group_by TEXT DEFAULT 'status',
    p_from_date DATE DEFAULT NULL,
    p_to_date DATE DEFAULT NULL)
RETURNS TABLE (
    group_value TEXT,
    contracts_count INT,
    total_amount NUMERIC,
    average_amount NUMERIC)
LANGUAGE sql
AS $$
    WITH base AS (
        SELECT
            c.id,
            c.status,
            cl.client_type,
            cu.code AS currency_code,
            cr.min_amount,
            cr.max_amount,
            cr.min_term_months,
            cr.max_term_months,
            c.contract_amount,
            c.issue_date,
            c.rate_type
        FROM contracts c
        JOIN clients cl ON cl.id = c.client_id
        JOIN credits cr ON cr.id = c.credit_id
        JOIN currencies cu ON cu.id = c.currency_id
        WHERE (p_from_date IS NULL OR c.issue_date >= p_from_date)
          AND (p_to_date IS NULL OR c.issue_date <= p_to_date)
    )
    SELECT
        CASE LOWER(COALESCE(p_group_by, 'status'))
            WHEN 'clienttype' THEN CASE base.client_type WHEN 'legal' THEN 'Юридическое лицо' ELSE 'Физическое лицо' END
            WHEN 'currency' THEN base.currency_code
            WHEN 'ratetype' THEN CASE base.rate_type WHEN 'fixed' THEN 'Фиксированная' ELSE 'Плавающая' END
            WHEN 'amountrange' THEN base.min_amount::TEXT || ' - ' || base.max_amount::TEXT
            WHEN 'termrange' THEN base.min_term_months::TEXT || ' - ' || base.max_term_months::TEXT || ' мес.'
            ELSE base.status
        END AS group_value,
        COUNT(*)::INT AS contracts_count,
        ROUND(SUM(base.contract_amount), 2) AS total_amount,
        ROUND(AVG(base.contract_amount), 2) AS average_amount
    FROM base
    GROUP BY 1
    ORDER BY contracts_count DESC, group_value;
$$;

-- Пункт 2: портрет кредитной нагрузки клиента.
-- Условие: нужны активные/завершенные договоры, суммы, средние показатели и доля просроченных платежей.
CREATE OR REPLACE FUNCTION report_client_credit_load()
RETURNS TABLE (
    client_id INT,
    client_display TEXT,
    client_type TEXT,
    active_contracts_count INT,
    completed_contracts_count INT,
    total_issued_amount NUMERIC,
    total_remaining_principal NUMERIC,
    average_term_months NUMERIC,
    average_interest_rate NUMERIC,
    overdue_payments_count INT,
    scheduled_payments_count INT,
    overdue_payment_share NUMERIC)
LANGUAGE sql
AS $$
    WITH contracts_base AS (
        SELECT
            c.id AS contract_id,
            cl.id AS client_id,
            CASE
                WHEN cl.client_type = 'legal' THEN lp.name
                ELSE pp.full_name
            END AS client_display,
            cl.client_type,
            c.status,
            c.contract_amount,
            c.remaining_principal,
            c.term_months,
            c.issue_date,
            COALESCE(c.fixed_interest_rate, 0) AS fixed_interest_rate
        FROM contracts c
        JOIN clients cl ON cl.id = c.client_id
        LEFT JOIN legal_persons lp ON lp.client_id = cl.id
        LEFT JOIN phys_persons pp ON pp.client_id = cl.id
        WHERE c.status <> 'Оформляется'
    ),
    payment_stats AS (
        SELECT
            p.contract_id,
            COUNT(*) FILTER (WHERE p.payment_date > p.planned_payment_date)::INT AS overdue_payments_count
        FROM payments p
        WHERE p.payment_type = 'monthly'
        GROUP BY p.contract_id
    ),
    schedule_stats AS (
        SELECT
            b.contract_id,
            COUNT(*)::INT AS scheduled_payments_count
        FROM contracts_base b
        CROSS JOIN LATERAL build_schedule(
            b.contract_amount,
            b.fixed_interest_rate / 100,
            b.term_months,
            b.issue_date) s
        GROUP BY b.contract_id
    )
    SELECT
        b.client_id,
        b.client_display,
        CASE WHEN b.client_type = 'legal' THEN 'Юридическое лицо' ELSE 'Физическое лицо' END AS client_type,
        COUNT(*) FILTER (WHERE b.status = 'Оформлен')::INT AS active_contracts_count,
        COUNT(*) FILTER (WHERE b.status = 'Завершён')::INT AS completed_contracts_count,
        ROUND(SUM(b.contract_amount), 2) AS total_issued_amount,
        ROUND(SUM(b.remaining_principal), 2) AS total_remaining_principal,
        ROUND(AVG(b.term_months), 2) AS average_term_months,
        ROUND(AVG(b.fixed_interest_rate), 2) AS average_interest_rate,
        COALESCE(SUM(ps.overdue_payments_count), 0)::INT AS overdue_payments_count,
        COALESCE(SUM(ss.scheduled_payments_count), 0)::INT AS scheduled_payments_count,
        CASE
            WHEN COALESCE(SUM(ss.scheduled_payments_count), 0) > 0 THEN
                ROUND(COALESCE(SUM(ps.overdue_payments_count), 0) * 100.0 / SUM(ss.scheduled_payments_count), 2)
            ELSE 0
        END AS overdue_payment_share
    FROM contracts_base b
    LEFT JOIN payment_stats ps ON ps.contract_id = b.contract_id
    LEFT JOIN schedule_stats ss ON ss.contract_id = b.contract_id
    GROUP BY b.client_id, b.client_display, b.client_type
    ORDER BY total_remaining_principal DESC, b.client_display;
$$;

-- Пункт 3: договоры с обеспечением и покрытием риска.
-- Условие: сумма залогов в валюте договора, коэффициент покрытия K и сведения о поручителях.
CREATE OR REPLACE FUNCTION report_contract_collateral()
RETURNS TABLE (
    contract_id INT,
    credit_name TEXT,
    client_display TEXT,
    currency_code TEXT,
    contract_amount NUMERIC,
    remaining_principal NUMERIC,
    pledge_value NUMERIC,
    coverage_coefficient NUMERIC,
    has_guarantors BOOLEAN,
    guarantor_count INT)
LANGUAGE sql
AS $$
    WITH base AS (
        SELECT
            c.id AS contract_id,
            cr.name AS credit_name,
            CASE
                WHEN cl.client_type = 'legal' THEN lp.name
                ELSE pp.full_name
            END AS client_display,
            cu.code AS currency_code,
            c.currency_id,
            c.contract_amount,
            c.remaining_principal
        FROM contracts c
        JOIN credits cr ON cr.id = c.credit_id
        JOIN currencies cu ON cu.id = c.currency_id
        JOIN clients cl ON cl.id = c.client_id
        LEFT JOIN legal_persons lp ON lp.client_id = cl.id
        LEFT JOIN phys_persons pp ON pp.client_id = cl.id
        WHERE c.status <> 'Оформляется'
    ),
    pledge_stats AS (
        SELECT
            b.contract_id,
            COALESCE(SUM(p.estimated_value), 0) AS pledge_value
        FROM base b
        LEFT JOIN pledges p ON p.contract_id = b.contract_id AND p.currency_id = b.currency_id
        GROUP BY b.contract_id
    ),
    guarantor_stats AS (
        SELECT
            g.contract_id,
            COUNT(*)::INT AS guarantor_count
        FROM guarantors g
        GROUP BY g.contract_id
    )
    SELECT
        b.contract_id,
        b.credit_name,
        b.client_display,
        b.currency_code,
        ROUND(b.contract_amount, 2) AS contract_amount,
        ROUND(b.remaining_principal, 2) AS remaining_principal,
        ROUND(COALESCE(ps.pledge_value, 0), 2) AS pledge_value,
        CASE
            WHEN b.remaining_principal > 0 THEN ROUND(COALESCE(ps.pledge_value, 0) / b.remaining_principal, 4)
            ELSE 0
        END AS coverage_coefficient,
        COALESCE(gs.guarantor_count, 0) > 0 AS has_guarantors,
        COALESCE(gs.guarantor_count, 0)::INT AS guarantor_count
    FROM base b
    LEFT JOIN pledge_stats ps ON ps.contract_id = b.contract_id
    LEFT JOIN guarantor_stats gs ON gs.contract_id = b.contract_id
    ORDER BY coverage_coefficient DESC, b.contract_id;
$$;

-- Пункт 4: клиенты с активными договорами.
-- Условие: количество договоров, общая выдача, остаток задолженности и средний ежемесячный платеж.
CREATE OR REPLACE FUNCTION report_active_clients()
RETURNS TABLE (
    client_id INT,
    client_display TEXT,
    active_contracts_count INT,
    total_issued_amount NUMERIC,
    total_remaining_principal NUMERIC,
    average_monthly_payment NUMERIC)
LANGUAGE sql
AS $$
    WITH base AS (
        SELECT
            c.id AS contract_id,
            cl.id AS client_id,
            CASE
                WHEN cl.client_type = 'legal' THEN lp.name
                ELSE pp.full_name
            END AS client_display,
            c.contract_amount,
            c.remaining_principal,
            c.term_months,
            c.issue_date,
            COALESCE(c.fixed_interest_rate, 0) AS fixed_interest_rate
        FROM contracts c
        JOIN clients cl ON cl.id = c.client_id
        LEFT JOIN legal_persons lp ON lp.client_id = cl.id
        LEFT JOIN phys_persons pp ON pp.client_id = cl.id
        WHERE c.status = 'Оформлен'
    ),
    monthly_pmt AS (
        SELECT
            b.contract_id,
            AVG(s.expected_payment) AS avg_payment
        FROM base b
        CROSS JOIN LATERAL build_schedule(
            b.contract_amount,
            b.fixed_interest_rate / 100,
            b.term_months,
            b.issue_date) s
        GROUP BY b.contract_id
    )
    SELECT
        b.client_id,
        b.client_display,
        COUNT(*)::INT AS active_contracts_count,
        ROUND(SUM(b.contract_amount), 2) AS total_issued_amount,
        ROUND(SUM(b.remaining_principal), 2) AS total_remaining_principal,
        ROUND(AVG(mp.avg_payment), 2) AS average_monthly_payment
    FROM base b
    LEFT JOIN monthly_pmt mp ON mp.contract_id = b.contract_id
    GROUP BY b.client_id, b.client_display
    ORDER BY total_remaining_principal DESC, b.client_display;
$$;

-- Пункт 5: сводка договоров по кредитному продукту.
-- Условие: количество договоров, суммарная выдача, средняя сумма и средний срок.
CREATE OR REPLACE FUNCTION report_credit_product_summary()
RETURNS TABLE (
    credit_id INT,
    credit_name TEXT,
    contracts_count INT,
    total_issued_amount NUMERIC,
    average_contract_amount NUMERIC,
    average_term_months NUMERIC)
LANGUAGE sql
AS $$
    SELECT
        cr.id AS credit_id,
        cr.name AS credit_name,
        COUNT(*)::INT AS contracts_count,
        ROUND(SUM(c.contract_amount), 2) AS total_issued_amount,
        ROUND(AVG(c.contract_amount), 2) AS average_contract_amount,
        ROUND(AVG(c.term_months), 2) AS average_term_months
    FROM contracts c
    JOIN credits cr ON cr.id = c.credit_id
    WHERE c.status <> 'Оформляется'
    GROUP BY cr.id, cr.name
    ORDER BY contracts_count DESC, cr.name;
$$;

-- Пункт 6: договоры с малым остатком долга.
-- Условие: отбор по порогу остатка от первоначальной суммы и показ ожидаемой даты завершения.
CREATE OR REPLACE FUNCTION report_nearing_completion_contracts(p_threshold_percent NUMERIC)
RETURNS TABLE (
    contract_id INT,
    credit_name TEXT,
    client_display TEXT,
    contract_amount NUMERIC,
    remaining_principal NUMERIC,
    repaid_percent NUMERIC,
    remaining_percent NUMERIC,
    expected_completion_date DATE)
LANGUAGE sql
AS $$
    WITH base AS (
        SELECT
            c.id AS contract_id,
            cr.name AS credit_name,
            CASE
                WHEN cl.client_type = 'legal' THEN lp.name
                ELSE pp.full_name
            END AS client_display,
            c.contract_amount,
            c.remaining_principal,
            c.term_months,
            c.issue_date,
            COALESCE(c.fixed_interest_rate, 0) AS fixed_interest_rate
        FROM contracts c
        JOIN credits cr ON cr.id = c.credit_id
        JOIN clients cl ON cl.id = c.client_id
        LEFT JOIN legal_persons lp ON lp.client_id = cl.id
        LEFT JOIN phys_persons pp ON pp.client_id = cl.id
        WHERE c.status <> 'Оформляется'
          AND p_threshold_percent > 0
          AND p_threshold_percent <= 100
    )
    SELECT
        b.contract_id,
        b.credit_name,
        b.client_display,
        ROUND(b.contract_amount, 2) AS contract_amount,
        ROUND(b.remaining_principal, 2) AS remaining_principal,
        ROUND((b.contract_amount - b.remaining_principal) * 100.0 / b.contract_amount, 2) AS repaid_percent,
        ROUND(b.remaining_principal * 100.0 / b.contract_amount, 2) AS remaining_percent,
        COALESCE(
            (
                SELECT MAX(s.planned_date)
                FROM build_schedule(
                    b.contract_amount,
                    b.fixed_interest_rate / 100,
                    b.term_months,
                    b.issue_date) s
            ),
            b.issue_date
        ) AS expected_completion_date
    FROM base b
    WHERE b.remaining_principal * 100.0 / b.contract_amount <= p_threshold_percent
    ORDER BY repaid_percent DESC, b.contract_id;
$$;

-- Пункт 7: расчет ожидаемых ежемесячных платежей на этапе оформления.
-- Условие: для заданных параметров договора вернуть ожидаемые платежи по графику.
CREATE OR REPLACE FUNCTION report_expected_payments(
    p_credit_id INT,
    p_currency_id INT,
    p_contract_amount NUMERIC,
    p_term_months INT,
    p_issue_date DATE)
RETURNS TABLE (
    installment_number INT,
    planned_date DATE,
    expected_payment NUMERIC
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_rate interest_rates%ROWTYPE;
    v_annual_percent NUMERIC;
BEGIN
    SELECT * INTO v_rate
    FROM interest_rates
    WHERE credit_id = p_credit_id
      AND currency_id = p_currency_id
      AND p_term_months BETWEEN term_from_months AND term_to_months
      AND p_issue_date >= valid_from
      AND (valid_to IS NULL OR p_issue_date <= valid_to)
    ORDER BY valid_from DESC
    LIMIT 1;

    IF v_rate.id IS NULL THEN
        RAISE EXCEPTION 'Не найдена ставка для заданных параметров';
    END IF;

    IF v_rate.rate_type = 'fixed' THEN
        v_annual_percent := COALESCE(v_rate.rate_value, 0);
    ELSE
        SELECT r.rate_percent + COALESCE(v_rate.additive_percent, 0)
        INTO v_annual_percent
        FROM refinance_rates r
        WHERE r.valid_from_date <= p_issue_date
          AND (r.valid_to_date IS NULL OR r.valid_to_date >= p_issue_date)
        ORDER BY r.valid_from_date DESC
        LIMIT 1;

        IF v_annual_percent IS NULL THEN
            RAISE EXCEPTION 'Не найдена ставка рефинансирования на дату оформления';
        END IF;
    END IF;

    RETURN QUERY
    SELECT s.installment_number, s.planned_date, s.expected_payment
    FROM build_schedule(p_contract_amount, v_annual_percent / 100, p_term_months, p_issue_date) s;
END;
$$;

-- Пункт 8: текущий долг по кредиту.
-- Условие: штраф за просрочку, проценты к оплате, часть основного долга к оплате и текущий остаток долга.
CREATE OR REPLACE FUNCTION report_current_debt(
    p_contract_id INT,
    p_on_date DATE DEFAULT CURRENT_DATE)
RETURNS TABLE (
    contract_id INT,
    late_penalty_accrued NUMERIC,
    interest_due NUMERIC,
    principal_due_this_period NUMERIC,
    remaining_principal NUMERIC
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_contract contracts%ROWTYPE;
    v_rate_fraction NUMERIC;
    v_last_payment_date DATE;
    v_accrual_start DATE;
    v_days INT;
    v_paid_count INT;
    v_planned_date DATE;
    v_principal_due NUMERIC;
    v_late_days INT;
BEGIN
    SELECT * INTO v_contract
    FROM contracts
    WHERE id = p_contract_id;

    IF v_contract.id IS NULL THEN
        RAISE EXCEPTION 'Договор % не найден', p_contract_id;
    END IF;

    IF v_contract.status = 'Оформляется' THEN
        RAISE EXCEPTION 'Договор еще не оформлен';
    END IF;

    v_rate_fraction := COALESCE(v_contract.fixed_interest_rate, 0) / 100;

    SELECT MAX(payment_date) INTO v_last_payment_date
    FROM payments
    WHERE contract_id = p_contract_id
      AND payment_type = 'monthly';

    IF v_last_payment_date IS NULL THEN
        v_accrual_start := first_accrual_date(v_contract.issue_date);
    ELSE
        v_accrual_start := v_last_payment_date + 1;
    END IF;

    v_days := GREATEST(1, p_on_date - v_accrual_start + 1);
    interest_due := ROUND(v_contract.remaining_principal * v_rate_fraction / 365 * v_days, 2);

    SELECT COUNT(*) INTO v_paid_count
    FROM payments
    WHERE contract_id = p_contract_id
      AND payment_type = 'monthly';

    IF v_paid_count < v_contract.term_months THEN
        SELECT s.planned_date, s.expected_principal
        INTO v_planned_date, v_principal_due
        FROM build_schedule(v_contract.contract_amount, v_rate_fraction, v_contract.term_months, v_contract.issue_date) s
        WHERE s.installment_number = v_paid_count + 1;
    ELSE
        SELECT s.planned_date
        INTO v_planned_date
        FROM build_schedule(v_contract.contract_amount, v_rate_fraction, v_contract.term_months, v_contract.issue_date) s
        ORDER BY s.installment_number DESC
        LIMIT 1;

        v_principal_due := v_contract.remaining_principal;
    END IF;

    v_late_days := GREATEST(0, p_on_date - v_planned_date);
    late_penalty_accrued := CASE
        WHEN v_late_days > 0 THEN ROUND((v_contract.remaining_principal + interest_due)
                                      * COALESCE(v_contract.fixed_late_penalty_z, 0)
                                      / 100
                                      * v_late_days, 2)
        ELSE 0
    END;

    contract_id := p_contract_id;
    principal_due_this_period := ROUND(v_principal_due, 2);
    remaining_principal := v_contract.remaining_principal;
    RETURN NEXT;
END;
$$;

-- Пункт 9: календарь платежей по договору.
-- Условие: плановая дата, ожидаемые суммы и статус выполнения (ожидается/выполнен/просрочен).
CREATE OR REPLACE FUNCTION report_payment_calendar(
    p_contract_id INT,
    p_on_date DATE DEFAULT CURRENT_DATE)
RETURNS TABLE (
    contract_id INT,
    planned_date DATE,
    expected_payment NUMERIC,
    expected_principal NUMERIC,
    expected_interest NUMERIC,
    status VARCHAR
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_contract contracts%ROWTYPE;
    v_annual_fraction NUMERIC;
BEGIN
    SELECT * INTO v_contract
    FROM contracts
    WHERE id = p_contract_id;

    IF v_contract.id IS NULL THEN
        RAISE EXCEPTION 'Договор % не найден', p_contract_id;
    END IF;

    v_annual_fraction := COALESCE(v_contract.fixed_interest_rate, 0) / 100;

    RETURN QUERY
    WITH sched AS (
        SELECT s.installment_number,
               s.planned_date,
               s.expected_payment,
               s.expected_principal,
               s.expected_interest
        FROM build_schedule(v_contract.contract_amount, v_annual_fraction, v_contract.term_months, v_contract.issue_date) s
    ),
    paid AS (
        SELECT p.planned_payment_date,
               MAX(p.payment_date) AS last_payment_date
        FROM payments p
        WHERE p.contract_id = p_contract_id
          AND p.payment_type = 'monthly'
        GROUP BY p.planned_payment_date
    )
    SELECT p_contract_id,
           s.planned_date,
           s.expected_payment,
           s.expected_principal,
           s.expected_interest,
           CASE
               WHEN p.last_payment_date IS NOT NULL THEN 'выполнен'
               WHEN p_on_date > s.planned_date THEN 'просрочен'
               ELSE 'ожидается'
           END::VARCHAR
    FROM sched s
    LEFT JOIN paid p ON p.planned_payment_date = s.planned_date
    ORDER BY s.installment_number;
END;
$$;

-- Пункт 10: история изменений условий кредитного продукта.
-- Условие: дата/время изменения, ставки и штрафы, предыдущие и новые параметры диапазонов сроков.
CREATE OR REPLACE FUNCTION report_credit_history(p_credit_id INT DEFAULT NULL)
RETURNS TABLE (
    change_date TIMESTAMP,
    kind VARCHAR,
    currency_code VARCHAR,
    old_value_percent NUMERIC,
    new_value_percent NUMERIC,
    old_term_from INT,
    old_term_to INT,
    new_term_from INT,
    new_term_to INT,
    penalty_type VARCHAR
)
LANGUAGE sql
AS $$
    SELECT
        h.change_date,
        'interest_rate'::VARCHAR,
        cu.code::VARCHAR,
        h.old_value,
        h.new_value,
        h.old_term_from,
        h.old_term_to,
        h.new_term_from,
        h.new_term_to,
        NULL::VARCHAR
    FROM rates_history h
    JOIN interest_rates r ON r.id = h.interest_rate_id
    JOIN currencies cu ON cu.id = r.currency_id
    WHERE p_credit_id IS NULL OR r.credit_id = p_credit_id

    UNION ALL

    SELECT
        h.change_date,
        'penalty'::VARCHAR,
        NULL::VARCHAR,
        h.old_value,
        h.new_value,
        NULL::INT,
        NULL::INT,
        NULL::INT,
        NULL::INT,
        p.penalty_type::VARCHAR
    FROM penalties_history h
    JOIN penalties p ON p.id = h.penalty_id
    WHERE p_credit_id IS NULL OR p.credit_id = p_credit_id;
$$;
