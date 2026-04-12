-- Требование 9: служебная функция проверки рабочего дня.
CREATE OR REPLACE FUNCTION is_weekday(p_date DATE)
RETURNS BOOLEAN
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT EXTRACT(ISODOW FROM p_date) BETWEEN 1 AND 5;
$$;

-- Требование 9: служебная функция вычисления последнего рабочего дня месяца.
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

-- Требования 7, 9: служебная функция первой даты начисления процентов.
CREATE OR REPLACE FUNCTION first_accrual_date(p_issue_date DATE)
RETURNS DATE
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT (date_trunc('month', p_issue_date) + interval '1 month')::DATE;
$$;

-- Требования 7, 9: служебная функция плановой даты платежа по номеру взноса.
CREATE OR REPLACE FUNCTION planned_payment_date_for_month(p_issue_date DATE, p_installment_index INT)
RETURNS DATE
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT last_workday_of_month((first_accrual_date(p_issue_date) + make_interval(months => p_installment_index))::DATE);
$$;

-- Требования 7, 8, 9: единый расчет графика платежей (аннуитет).
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

-- Требование 1: ввод и изменение описаний условий кредитных продуктов.
CREATE OR REPLACE PROCEDURE upsert_credit(
    p_id INT,
    p_name VARCHAR,
    p_desc TEXT,
    p_type VARCHAR,
    p_min NUMERIC,
    p_max NUMERIC,
    p_min_term INT,
    p_max_term INT
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_id IS NULL THEN
        INSERT INTO credits(name, description, client_type, min_amount, max_amount, min_term_months, max_term_months)
        VALUES (p_name, p_desc, p_type, p_min, p_max, p_min_term, p_max_term);
    ELSE
        UPDATE credits
        SET name = p_name,
            description = p_desc,
            client_type = p_type,
            min_amount = p_min,
            max_amount = p_max,
            min_term_months = p_min_term,
            max_term_months = p_max_term
        WHERE id = p_id;
    END IF;
END;
$$;

-- Требование 1, 10: ввод/изменение ставок с сохранением истории условий.
CREATE OR REPLACE PROCEDURE upsert_interest_rate(
    p_id INT,
    p_credit_id INT,
    p_currency_id INT,
    p_term_from INT,
    p_term_to INT,
    p_rate_type VARCHAR,
    p_rate_value NUMERIC,
    p_additive_percent NUMERIC,
    p_valid_from DATE,
    p_valid_to DATE
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_old interest_rates%ROWTYPE;
    v_old_value NUMERIC;
    v_new_value NUMERIC;
BEGIN
    IF p_id IS NULL THEN
        INSERT INTO interest_rates(
            credit_id, currency_id, term_from_months, term_to_months,
            rate_type, rate_value, additive_percent, valid_from, valid_to)
        VALUES (
            p_credit_id,
            p_currency_id,
            p_term_from,
            p_term_to,
            p_rate_type,
            CASE WHEN p_rate_type = 'fixed' THEN p_rate_value ELSE NULL END,
            CASE WHEN p_rate_type = 'floating' THEN p_additive_percent ELSE NULL END,
            p_valid_from,
            p_valid_to
        );
    ELSE
        SELECT * INTO v_old
        FROM interest_rates
        WHERE id = p_id;

        IF v_old.id IS NULL THEN
            RAISE EXCEPTION 'Процентная ставка % не найдена', p_id;
        END IF;

        v_old_value := CASE
            WHEN v_old.rate_type = 'fixed' THEN v_old.rate_value
            ELSE v_old.additive_percent
        END;

        v_new_value := CASE
            WHEN p_rate_type = 'fixed' THEN p_rate_value
            ELSE p_additive_percent
        END;

        INSERT INTO rates_history(
            interest_rate_id,
            change_date,
            old_value,
            new_value,
            old_term_from,
            old_term_to,
            new_term_from,
            new_term_to
        )
        VALUES (
            p_id,
            CURRENT_TIMESTAMP,
            v_old_value,
            COALESCE(v_new_value, 0),
            v_old.term_from_months,
            v_old.term_to_months,
            p_term_from,
            p_term_to
        );

        UPDATE interest_rates
        SET credit_id = p_credit_id,
            currency_id = p_currency_id,
            term_from_months = p_term_from,
            term_to_months = p_term_to,
            rate_type = p_rate_type,
            rate_value = CASE WHEN p_rate_type = 'fixed' THEN p_rate_value ELSE NULL END,
            additive_percent = CASE WHEN p_rate_type = 'floating' THEN p_additive_percent ELSE NULL END,
            valid_from = p_valid_from,
            valid_to = p_valid_to
        WHERE id = p_id;
    END IF;
END;
$$;

-- Требование 1, 10: ввод/изменение штрафов с сохранением истории условий.
CREATE OR REPLACE PROCEDURE upsert_penalty(
    p_id INT,
    p_credit_id INT,
    p_penalty_type VARCHAR,
    p_value_percent NUMERIC,
    p_valid_from DATE
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_old penalties%ROWTYPE;
BEGIN
    IF p_id IS NULL THEN
        INSERT INTO penalties(credit_id, penalty_type, value_percent, valid_from)
        VALUES (p_credit_id, p_penalty_type, p_value_percent, p_valid_from);
    ELSE
        SELECT * INTO v_old
        FROM penalties
        WHERE id = p_id;

        IF v_old.id IS NULL THEN
            RAISE EXCEPTION 'Штраф % не найден', p_id;
        END IF;

        INSERT INTO penalties_history(penalty_id, change_date, old_value, new_value)
        VALUES (p_id, CURRENT_TIMESTAMP, v_old.value_percent, p_value_percent);

        UPDATE penalties
        SET credit_id = p_credit_id,
            penalty_type = p_penalty_type,
            value_percent = p_value_percent,
            valid_from = p_valid_from
        WHERE id = p_id;
    END IF;
END;
$$;

-- Требование 2: ввод/изменение данных физических клиентов.
CREATE OR REPLACE PROCEDURE upsert_physical_client(
    p_id INT,
    p_name VARCHAR,
    p_series VARCHAR,
    p_number VARCHAR,
    p_address TEXT,
    p_phone VARCHAR
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_id IS NULL THEN
        INSERT INTO clients(client_type)
        VALUES ('physical')
        RETURNING id INTO p_id;

        INSERT INTO phys_persons(client_id, full_name, passport_series, passport_number, actual_address, phone)
        VALUES (p_id, p_name, UPPER(TRIM(p_series)), TRIM(p_number), p_address, p_phone);
    ELSE
        UPDATE phys_persons
        SET full_name = p_name,
            passport_series = UPPER(TRIM(p_series)),
            passport_number = TRIM(p_number),
            actual_address = p_address,
            phone = p_phone
        WHERE client_id = p_id;
    END IF;
END;
$$;

-- Требование 2: ввод/изменение данных юридических клиентов.
CREATE OR REPLACE PROCEDURE upsert_legal_client(
    p_id INT,
    p_name VARCHAR,
    p_ownership_type VARCHAR,
    p_legal_address TEXT,
    p_phone VARCHAR
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_id IS NULL THEN
        INSERT INTO clients(client_type)
        VALUES ('legal')
        RETURNING id INTO p_id;

        INSERT INTO legal_persons(client_id, name, ownership_type, legal_address, phone)
        VALUES (p_id, p_name, p_ownership_type, p_legal_address, p_phone);
    ELSE
        UPDATE legal_persons
        SET name = p_name,
            ownership_type = p_ownership_type,
            legal_address = p_legal_address,
            phone = p_phone
        WHERE client_id = p_id;
    END IF;
END;
$$;

-- Требование 3: ввод/изменение данных валют.
CREATE OR REPLACE PROCEDURE upsert_currency(
    p_id INT,
    p_code VARCHAR,
    p_name VARCHAR
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_id IS NULL THEN
        INSERT INTO currencies(code, name)
        VALUES (UPPER(TRIM(p_code)), p_name);
    ELSE
        UPDATE currencies
        SET code = UPPER(TRIM(p_code)),
            name = p_name
        WHERE id = p_id;
    END IF;
END;
$$;

-- Требование 4: ввод/изменение данных о поручителях.
CREATE OR REPLACE PROCEDURE upsert_guarantor(
    p_id INT,
    p_contract INT,
    p_phys_id INT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_status VARCHAR;
BEGIN
    SELECT status INTO v_status
    FROM contracts
    WHERE id = p_contract;

    IF v_status IS NULL THEN
        RAISE EXCEPTION 'Договор % не найден', p_contract;
    END IF;

    IF v_status <> 'Оформляется' THEN
        RAISE EXCEPTION 'Поручителя можно менять только в статусе «Оформляется»';
    END IF;

    IF p_id IS NULL THEN
        INSERT INTO guarantors(contract_id, phys_person_id)
        VALUES (p_contract, p_phys_id);
    ELSE
        UPDATE guarantors
        SET contract_id = p_contract,
            phys_person_id = p_phys_id
        WHERE id = p_id;
    END IF;
END;
$$;

-- Требование 4: добавление поручителя (совместимость).
CREATE OR REPLACE PROCEDURE add_guarantor(
    p_contract INT,
    p_phys_id INT
)
LANGUAGE plpgsql
AS $$
BEGIN
    CALL upsert_guarantor(NULL, p_contract, p_phys_id);
END;
$$;

-- Требование 5: оформление кредитного договора (создание черновика).
CREATE OR REPLACE PROCEDURE create_contract_full(
    p_client INT,
    p_credit INT,
    p_currency INT,
    p_amount NUMERIC,
    p_term INT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_rate interest_rates%ROWTYPE;
    v_early NUMERIC;
    v_late NUMERIC;
    v_ref_rate NUMERIC;
    v_final_rate NUMERIC;
BEGIN
    SELECT * INTO v_rate
    FROM interest_rates
    WHERE credit_id = p_credit
      AND currency_id = p_currency
      AND p_term BETWEEN term_from_months AND term_to_months
      AND CURRENT_DATE >= valid_from
      AND (valid_to IS NULL OR CURRENT_DATE <= valid_to)
    ORDER BY valid_from DESC
    LIMIT 1;

    IF v_rate.id IS NULL THEN
        RAISE EXCEPTION 'Не найдена подходящая процентная ставка';
    END IF;

    SELECT value_percent INTO v_early
    FROM penalties
    WHERE credit_id = p_credit
      AND penalty_type = 'early_repayment'
      AND valid_from <= CURRENT_DATE
    ORDER BY valid_from DESC
    LIMIT 1;

    SELECT value_percent INTO v_late
    FROM penalties
    WHERE credit_id = p_credit
      AND penalty_type = 'late_payment'
      AND valid_from <= CURRENT_DATE
    ORDER BY valid_from DESC
    LIMIT 1;

    IF v_rate.rate_type = 'floating' THEN
        SELECT rate_percent INTO v_ref_rate
        FROM refinance_rates
        WHERE valid_from_date <= CURRENT_DATE
          AND (valid_to_date IS NULL OR valid_to_date >= CURRENT_DATE)
        ORDER BY valid_from_date DESC
        LIMIT 1;

        IF v_ref_rate IS NULL THEN
            RAISE EXCEPTION 'Не найдена актуальная ставка рефинансирования';
        END IF;

        v_final_rate := v_ref_rate + COALESCE(v_rate.additive_percent, 0);
    ELSE
        v_final_rate := v_rate.rate_value;
    END IF;

    INSERT INTO contracts(
        client_id,
        credit_id,
        currency_id,
        contract_amount,
        term_months,
        issue_date,
        status,
        remaining_principal,
        fixed_interest_rate,
        fixed_additive_percent,
        fixed_early_penalty_x,
        fixed_late_penalty_z,
        rate_type,
        interest_rate_id
    )
    VALUES (
        p_client,
        p_credit,
        p_currency,
        p_amount,
        p_term,
        CURRENT_DATE,
        'Оформляется',
        p_amount,
        v_final_rate,
        COALESCE(v_rate.additive_percent, 0),
        COALESCE(v_early, 0),
        COALESCE(v_late, 0),
        v_rate.rate_type,
        v_rate.id
    );
END;
$$;

-- Требование 5: изменение черновика договора.
CREATE OR REPLACE PROCEDURE update_contract_draft(
    p_contract_id INT,
    p_client INT,
    p_credit INT,
    p_currency INT,
    p_amount NUMERIC,
    p_term INT,
    p_issue_date DATE
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_status VARCHAR;
BEGIN
    SELECT status INTO v_status
    FROM contracts
    WHERE id = p_contract_id;

    IF v_status IS NULL THEN
        RAISE EXCEPTION 'Договор % не найден', p_contract_id;
    END IF;

    IF v_status <> 'Оформляется' THEN
        RAISE EXCEPTION 'Изменение доступно только для статуса «Оформляется»';
    END IF;

    UPDATE contracts
    SET client_id = p_client,
        credit_id = p_credit,
        currency_id = p_currency,
        contract_amount = p_amount,
        term_months = p_term,
        issue_date = p_issue_date,
        remaining_principal = p_amount
    WHERE id = p_contract_id;
END;
$$;

-- Требование 5: удаление черновика договора.
CREATE OR REPLACE PROCEDURE delete_contract_draft(p_contract_id INT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_status VARCHAR;
BEGIN
    SELECT status INTO v_status
    FROM contracts
    WHERE id = p_contract_id;

    IF v_status IS NULL THEN
        RAISE EXCEPTION 'Договор % не найден', p_contract_id;
    END IF;

    IF v_status <> 'Оформляется' THEN
        RAISE EXCEPTION 'Удаление доступно только для статуса «Оформляется»';
    END IF;

    DELETE FROM payments WHERE contract_id = p_contract_id;
    DELETE FROM guarantors WHERE contract_id = p_contract_id;
    DELETE FROM pledges WHERE contract_id = p_contract_id;
    DELETE FROM contracts WHERE id = p_contract_id;
END;
$$;

-- Требование 5: перевод договора из «Оформляется» в «Оформлен».
CREATE OR REPLACE PROCEDURE sign_contract(p_contract_id INT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_status VARCHAR;
BEGIN
    SELECT status INTO v_status
    FROM contracts
    WHERE id = p_contract_id;

    IF v_status IS NULL THEN
        RAISE EXCEPTION 'Договор % не найден', p_contract_id;
    END IF;

    IF v_status <> 'Оформляется' THEN
        RAISE EXCEPTION 'Оформление доступно только из статуса «Оформляется»';
    END IF;

    UPDATE contracts
    SET status = 'Оформлен',
        remaining_principal = contract_amount
    WHERE id = p_contract_id;
END;
$$;

-- Требование 6: оформление платежа по кредиту с контролем статуса.
CREATE OR REPLACE PROCEDURE make_payment_full(
    p_contract INT,
    p_payment NUMERIC
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_contract contracts%ROWTYPE;
    v_interest NUMERIC;
    v_rate_fraction NUMERIC;
    v_remain NUMERIC;
BEGIN
    SELECT * INTO v_contract
    FROM contracts
    WHERE id = p_contract;

    IF v_contract.id IS NULL THEN
        RAISE EXCEPTION 'Договор % не найден', p_contract;
    END IF;

    IF v_contract.status <> 'Оформлен' THEN
        RAISE EXCEPTION 'Платежи доступны только для договоров в статусе «Оформлен»';
    END IF;

    v_rate_fraction := COALESCE(v_contract.fixed_interest_rate, 0) / 100;
    v_interest := ROUND(v_contract.remaining_principal * v_rate_fraction / 365, 2);
    v_remain := ROUND(v_contract.remaining_principal - (p_payment - v_interest), 2);

    INSERT INTO payments(
        contract_id,
        payment_date,
        planned_payment_date,
        payment_type,
        principal_amount,
        interest_amount,
        applied_annual_rate,
        total_amount,
        remaining_after_payment
    )
    VALUES (
        p_contract,
        CURRENT_DATE,
        CURRENT_DATE,
        'monthly',
        ROUND(p_payment - v_interest, 2),
        v_interest,
        v_rate_fraction,
        p_payment,
        GREATEST(0, v_remain)
    );

    IF v_remain <= 0.01 THEN
        UPDATE contracts
        SET status = 'Завершён',
            remaining_principal = 0
        WHERE id = p_contract;
    END IF;
END;
$$;

-- Требование 7: отчет ожидаемых ежемесячных платежей на этапе оформления.
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

-- Требование 8: отчет текущего долга по кредиту.
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

-- Требование 9: отчет календаря платежей по договору.
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

-- Требование 10: отчет истории изменений условий кредитного продукта.
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

-- Требование 7: view ожидаемых платежей по всем договорам.
CREATE OR REPLACE VIEW expected_payments AS
SELECT
    c.id AS contract_id,
    s.installment_number,
    s.planned_date,
    s.expected_payment,
    s.expected_principal,
    s.expected_interest
FROM contracts c
CROSS JOIN LATERAL build_schedule(
    c.contract_amount,
    COALESCE(c.fixed_interest_rate, 0) / 100,
    c.term_months,
    c.issue_date
) s;

-- Требование 8: view текущего долга по всем оформленным договорам.
CREATE OR REPLACE VIEW current_debt_report AS
SELECT
    c.id AS contract_id,
    d.late_penalty_accrued,
    d.interest_due,
    d.principal_due_this_period,
    d.remaining_principal
FROM contracts c
CROSS JOIN LATERAL report_current_debt(c.id, CURRENT_DATE) d
WHERE c.status <> 'Оформляется';

-- Требование 9: view календаря платежей по всем договорам.
CREATE OR REPLACE VIEW payment_calendar AS
SELECT
    c.id AS contract_id,
    p.planned_date,
    p.expected_payment,
    p.expected_principal,
    p.expected_interest,
    p.status
FROM contracts c
CROSS JOIN LATERAL report_payment_calendar(c.id, CURRENT_DATE) p;

-- Требование 10: view истории изменений условий кредитных продуктов.
CREATE OR REPLACE VIEW credit_history_report AS
SELECT
    h.change_date,
    h.kind,
    h.currency_code,
    h.old_value_percent,
    h.new_value_percent,
    h.old_term_from,
    h.old_term_to,
    h.new_term_from,
    h.new_term_to,
    h.penalty_type
FROM report_credit_history(NULL) h
ORDER BY h.change_date DESC;

-- Дополнительные отчеты для клиентского приложения.

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
