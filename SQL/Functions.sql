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
        INSERT INTO credits
        VALUES (DEFAULT, p_name, p_desc, p_type,
                p_min, p_max, p_min_term, p_max_term);
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

        INSERT INTO phys_persons
        VALUES (p_id, p_name, p_series, p_number, p_address, p_phone);
    ELSE
        UPDATE phys_persons
        SET full_name = p_name,
            passport_series = p_series,
            passport_number = p_number,
            actual_address = p_address,
            phone = p_phone
        WHERE client_id = p_id;
    END IF;
END;
$$;



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
        VALUES (p_code, p_name);
    ELSE
        UPDATE currencies
        SET code = p_code,
            name = p_name
        WHERE id = p_id;
    END IF;
END;
$$;



CREATE OR REPLACE PROCEDURE add_guarantor(
    p_contract INT,
    p_phys_id INT
)
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO guarantors(contract_id, phys_person_id)
    VALUES (p_contract, p_phys_id);
END;
$$;



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
    v_rate RECORD;
    v_early NUMERIC;
    v_late NUMERIC;
    v_ref_rate NUMERIC;
    v_final_rate NUMERIC;
BEGIN
    -- ставка
    SELECT * INTO v_rate
    FROM interest_rates
    WHERE credit_id = p_credit
      AND currency_id = p_currency
      AND p_term BETWEEN term_from_months AND term_to_months
      AND CURRENT_DATE >= valid_from
    ORDER BY valid_from DESC
    LIMIT 1;

    IF v_rate IS NULL THEN
        RAISE EXCEPTION 'Не найдена подходящая процентная ставка';
    END IF;

    -- штраф за досрочное
    SELECT value_percent INTO v_early
    FROM penalties
    WHERE credit_id = p_credit
      AND penalty_type = 'early_repayment'
    ORDER BY valid_from DESC
    LIMIT 1;

    -- штраф за просрочку
    SELECT value_percent INTO v_late
    FROM penalties
    WHERE credit_id = p_credit
      AND penalty_type = 'late_payment'
    ORDER BY valid_from DESC
    LIMIT 1;

    -- если плавающая ставка, добавляем ставку рефинансирования
    IF v_rate.rate_type = 'floating' THEN
        SELECT rate_percent INTO v_ref_rate
        FROM refinance_rates
        WHERE valid_from_date <= CURRENT_DATE
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
        client_id, credit_id, currency_id,
        contract_amount, term_months,
        issue_date, remaining_principal,
        fixed_interest_rate,
        fixed_additive_percent,
        fixed_early_penalty_x,
        fixed_late_penalty_z,
        rate_type,
        interest_rate_id
    )
    VALUES (
        p_client, p_credit, p_currency,
        p_amount, p_term,
        CURRENT_DATE, p_amount,
        v_final_rate,
        COALESCE(v_rate.additive_percent, 0),
        COALESCE(v_early, 0),
        COALESCE(v_late, 0),
        v_rate.rate_type,
        v_rate.id
    );
END;
$$;



CREATE OR REPLACE PROCEDURE make_payment_full(
    p_contract INT,
    p_payment NUMERIC
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_remain NUMERIC;
    v_interest NUMERIC;
    v_rate NUMERIC;
BEGIN
    SELECT remaining_principal, fixed_interest_rate
    INTO v_remain, v_rate
    FROM contracts
    WHERE id = p_contract;

    IF v_rate IS NULL THEN
        RAISE EXCEPTION 'Не задана процентная ставка в договоре';
    END IF;

    v_interest := v_remain * v_rate / 365;

    v_remain := v_remain - (p_payment - v_interest);

    INSERT INTO payments(
        contract_id,
        payment_date,
        planned_payment_date,
        payment_type,
        principal_amount,
        interest_amount,
        total_amount,
        remaining_after_payment
    )
    VALUES (
        p_contract,
        CURRENT_DATE,
        CURRENT_DATE,
        'monthly',
        p_payment - v_interest,
        v_interest,
        p_payment,
        v_remain
    );
END;
$$;



CREATE OR REPLACE VIEW expected_payments AS
SELECT
    c.id,
    c.contract_amount / c.term_months AS monthly_principal
FROM contracts c;



CREATE OR REPLACE VIEW current_debt_report AS
SELECT
    c.id,
    c.remaining_principal,
    SUM(p.interest_amount) AS interest_due,
    SUM(p.late_penalty) AS penalties
FROM contracts c
LEFT JOIN payments p ON p.contract_id = c.id
GROUP BY c.id;



CREATE OR REPLACE VIEW payment_calendar AS
SELECT
    c.id AS contract_id,
    generate_series(
        date_trunc('month', c.issue_date) + interval '1 month',
        c.issue_date + (c.term_months || ' months')::interval,
        interval '1 month'
    )::date AS planned_date
FROM contracts c;



CREATE OR REPLACE VIEW credit_history_report AS
SELECT
    r.change_date,
    r.old_value,
    r.new_value,
    r.old_term_from,
    r.old_term_to,
    r.new_term_from,
    r.new_term_to
FROM rates_history r;