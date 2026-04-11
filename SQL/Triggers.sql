CREATE OR REPLACE FUNCTION check_client_credit_match()
RETURNS TRIGGER AS $$
DECLARE
    client_type_val VARCHAR;
    credit_type_val VARCHAR;
BEGIN
    SELECT client_type INTO client_type_val
    FROM clients WHERE id = NEW.client_id;

    SELECT client_type INTO credit_type_val
    FROM credits WHERE id = NEW.credit_id;

    IF client_type_val <> credit_type_val THEN
        RAISE EXCEPTION 'Тип клиента не совпадает с типом кредита';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_check_client_credit
BEFORE INSERT ON contracts
FOR EACH ROW
EXECUTE FUNCTION check_client_credit_match();



CREATE OR REPLACE FUNCTION check_contract_limits()
RETURNS TRIGGER AS $$
DECLARE
    v_min NUMERIC;
    v_max NUMERIC;
    v_min_term INT;
    v_max_term INT;
BEGIN
    SELECT min_amount, max_amount, min_term_months, max_term_months
    INTO v_min, v_max, v_min_term, v_max_term
    FROM credits
    WHERE id = NEW.credit_id;

    IF NEW.contract_amount < v_min OR NEW.contract_amount > v_max THEN
        RAISE EXCEPTION 'Сумма вне диапазона';
    END IF;

    IF NEW.term_months < v_min_term OR NEW.term_months > v_max_term THEN
        RAISE EXCEPTION 'Срок вне диапазона';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_check_limits
BEFORE INSERT ON contracts
FOR EACH ROW
EXECUTE FUNCTION check_contract_limits();



CREATE OR REPLACE FUNCTION update_remaining()
RETURNS TRIGGER AS $$
BEGIN
    UPDATE contracts
    SET remaining_principal = NEW.remaining_after_payment
    WHERE id = NEW.contract_id;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_update_remaining
AFTER INSERT ON payments
FOR EACH ROW
EXECUTE FUNCTION update_remaining();



CREATE OR REPLACE FUNCTION check_guarantor_allowed()
RETURNS TRIGGER AS $$
DECLARE
    client_type_val VARCHAR;
BEGIN
    SELECT c.client_type INTO client_type_val
    FROM contracts ct
    JOIN clients c ON ct.client_id = c.id
    WHERE ct.id = NEW.contract_id;

    IF client_type_val <> 'physical' THEN
        RAISE EXCEPTION 'Поручители только для физических лиц';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_check_guarantor
BEFORE INSERT ON guarantors
FOR EACH ROW
EXECUTE FUNCTION check_guarantor_allowed();



CREATE OR REPLACE FUNCTION validate_refinance_rate_period()
RETURNS TRIGGER AS $$
DECLARE
    v_previous_open_ended_id INT;
BEGIN
    IF NEW.valid_to_date IS NOT NULL AND NEW.valid_to_date < NEW.valid_from_date THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            CONSTRAINT = 'chk_refinance_rates_date_order',
            MESSAGE = 'Дата начала не может быть позже даты окончания.';
    END IF;

    IF TG_OP = 'INSERT' AND NEW.valid_to_date IS NULL THEN
        SELECT r.id
        INTO v_previous_open_ended_id
        FROM refinance_rates r
        WHERE r.valid_to_date IS NULL
          AND r.valid_from_date < NEW.valid_from_date
        ORDER BY r.valid_from_date DESC
        LIMIT 1;

        IF v_previous_open_ended_id IS NOT NULL THEN
            UPDATE refinance_rates
            SET valid_to_date = NEW.valid_from_date - 1
            WHERE id = v_previous_open_ended_id;
        END IF;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM refinance_rates r
        WHERE r.id <> COALESCE(NEW.id, 0)
          AND r.valid_from_date >= NEW.valid_from_date
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            CONSTRAINT = 'chk_refinance_rates_start_after_existing',
            MESSAGE = 'Дата начала новой ставки должна быть позже дат начала всех существующих ставок.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM refinance_rates r
        WHERE r.id <> COALESCE(NEW.id, 0)
          AND daterange(r.valid_from_date, COALESCE(r.valid_to_date, 'infinity'::date), '[]')
              && daterange(NEW.valid_from_date, COALESCE(NEW.valid_to_date, 'infinity'::date), '[]')
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            CONSTRAINT = 'chk_refinance_rates_no_overlap',
            MESSAGE = 'Период ставки рефинансирования пересекается с уже существующим периодом.';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_validate_refinance_rate_period
BEFORE INSERT OR UPDATE ON refinance_rates
FOR EACH ROW
EXECUTE FUNCTION validate_refinance_rate_period();


CREATE OR REPLACE FUNCTION validate_interest_rate_overlap()
RETURNS TRIGGER AS $$
DECLARE
    v_min_term INT;
    v_max_term INT;
BEGIN
    SELECT c.min_term_months, c.max_term_months
    INTO v_min_term, v_max_term
    FROM credits c
    WHERE c.id = NEW.credit_id;

    IF v_min_term IS NULL OR v_max_term IS NULL THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            CONSTRAINT = 'chk_interest_rates_within_credit_term_bounds',
            MESSAGE = 'Кредитный продукт не найден для проверки диапазона сроков.';
    END IF;

    IF NEW.term_from_months < v_min_term
       OR NEW.term_from_months > v_max_term
       OR NEW.term_to_months < v_min_term
       OR NEW.term_to_months > v_max_term THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            CONSTRAINT = 'chk_interest_rates_within_credit_term_bounds',
            MESSAGE = 'Диапазон сроков ставки должен находиться в границах срока кредитного продукта.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM interest_rates r
        WHERE r.id <> COALESCE(NEW.id, 0)
          AND r.credit_id = NEW.credit_id
          AND r.currency_id = NEW.currency_id
          AND int4range(r.term_from_months, r.term_to_months, '[]')
              && int4range(NEW.term_from_months, NEW.term_to_months, '[]')
          AND daterange(r.valid_from, COALESCE(r.valid_to, 'infinity'::date), '[]')
              && daterange(NEW.valid_from, COALESCE(NEW.valid_to, 'infinity'::date), '[]')
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            CONSTRAINT = 'chk_interest_rates_no_overlap',
            MESSAGE = 'Обнаружено пересечение процентных ставок для одного продукта и валюты.';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_validate_interest_rate_overlap
BEFORE INSERT OR UPDATE ON interest_rates
FOR EACH ROW
EXECUTE FUNCTION validate_interest_rate_overlap();


CREATE OR REPLACE FUNCTION validate_penalty_rules()
RETURNS TRIGGER AS $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM penalties p
        WHERE p.valid_from > NEW.valid_from
          AND p.credit_id = NEW.credit_id
          AND p.penalty_type = NEW.penalty_type
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            CONSTRAINT = 'chk_penalties_chronological_order',
            MESSAGE = 'Новый штраф должен иметь дату не раньше существующих штрафов того же типа.';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_validate_penalty_rules
BEFORE INSERT OR UPDATE ON penalties
FOR EACH ROW
EXECUTE FUNCTION validate_penalty_rules();
