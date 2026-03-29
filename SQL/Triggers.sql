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



CREATE OR REPLACE FUNCTION lock_contract_after_sign()
RETURNS TRIGGER AS $$
BEGIN
    -- нельзя изменять договор, если он уже Оформлен или Завершён
    IF OLD.status IN ('Оформлен', 'Завершён') THEN
        RAISE EXCEPTION 'Нельзя изменять договор после оформления';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_lock_contract_after_sign
BEFORE UPDATE ON contracts
FOR EACH ROW
EXECUTE FUNCTION lock_contract_after_sign();