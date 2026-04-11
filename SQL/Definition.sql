CREATE TABLE clients (
    id SERIAL PRIMARY KEY,
    client_type VARCHAR(20) NOT NULL
);

ALTER TABLE clients 
ADD CONSTRAINT chk_clients_client_type 
CHECK (client_type IN ('legal', 'physical'));

CREATE TABLE legal_persons (
    client_id INTEGER PRIMARY KEY REFERENCES clients(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    ownership_type VARCHAR(50) NOT NULL,
    legal_address TEXT NOT NULL,
    phone VARCHAR(20)
);

CREATE TABLE phys_persons (
    client_id INTEGER PRIMARY KEY REFERENCES clients(id) ON DELETE CASCADE,
    full_name VARCHAR(255) NOT NULL,
    passport_series VARCHAR(2) NOT NULL,
    passport_number VARCHAR(7) NOT NULL,
    actual_address TEXT,
    phone VARCHAR(20)
);

ALTER TABLE phys_persons
ADD CONSTRAINT uq_phys_persons_passport
UNIQUE (passport_series, passport_number);

ALTER TABLE phys_persons
ADD CONSTRAINT uq_phys_persons_phone
UNIQUE (phone);

CREATE TABLE credits (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    client_type VARCHAR(20) NOT NULL,
    min_amount DECIMAL(15,2) NOT NULL,
    max_amount DECIMAL(15,2) NOT NULL,
    min_term_months INTEGER NOT NULL,
    max_term_months INTEGER NOT NULL
);

ALTER TABLE credits 
ADD CONSTRAINT chk_credits_client_type 
CHECK (client_type IN ('legal', 'physical'));

ALTER TABLE credits 
ADD CONSTRAINT chk_credits_min_amount 
CHECK (min_amount > 0);

ALTER TABLE credits 
ADD CONSTRAINT chk_credits_max_amount 
CHECK (max_amount >= min_amount);

ALTER TABLE credits 
ADD CONSTRAINT chk_credits_min_term 
CHECK (min_term_months > 0);

ALTER TABLE credits 
ADD CONSTRAINT chk_credits_max_term 
CHECK (max_term_months >= min_term_months);

CREATE TABLE currencies (
    id SERIAL PRIMARY KEY,
    code VARCHAR(3) UNIQUE NOT NULL,
    name VARCHAR(50) NOT NULL
);

ALTER TABLE currencies
ADD CONSTRAINT chk_currencies_code_letters_only
CHECK (code ~ '^[A-Za-z]{3}$');

CREATE TABLE credit_currencies (
    credit_id INTEGER REFERENCES credits(id),
    currency_id INTEGER REFERENCES currencies(id),
    PRIMARY KEY (credit_id, currency_id)
);

CREATE TABLE interest_rates (
    id SERIAL PRIMARY KEY,
    credit_id INTEGER REFERENCES credits(id),
    currency_id INTEGER REFERENCES currencies(id),
    term_from_months INTEGER NOT NULL,
    term_to_months INTEGER NOT NULL,
    rate_type VARCHAR(20) NOT NULL,
    rate_value DECIMAL(5,4),
    additive_percent DECIMAL(5,4),
    valid_from DATE NOT NULL,
    valid_to DATE
);

ALTER TABLE interest_rates 
ADD CONSTRAINT chk_interest_rates_term_from 
CHECK (term_from_months > 0);

ALTER TABLE interest_rates 
ADD CONSTRAINT chk_interest_rates_term_range 
CHECK (term_to_months >= term_from_months);

ALTER TABLE interest_rates 
ADD CONSTRAINT chk_interest_rates_rate_type 
CHECK (rate_type IN ('fixed', 'floating'));

ALTER TABLE interest_rates 
ADD CONSTRAINT chk_interest_rates_rate_value_non_negative 
CHECK (rate_value IS NULL OR rate_value >= 0);

ALTER TABLE interest_rates 
ADD CONSTRAINT chk_interest_rates_additive_percent_non_negative 
CHECK (additive_percent IS NULL OR additive_percent >= 0);

ALTER TABLE interest_rates 
ADD CONSTRAINT chk_interest_rates_date_order
CHECK (valid_to IS NULL OR valid_from <= valid_to);

ALTER TABLE interest_rates 
ADD CONSTRAINT chk_interest_rates_rate_rules 
CHECK (
    (rate_type = 'fixed' AND rate_value IS NOT NULL AND additive_percent IS NULL) OR
    (rate_type = 'floating' AND additive_percent IS NOT NULL AND rate_value IS NULL)
);

ALTER TABLE interest_rates
ADD CONSTRAINT fk_interest_rates_credit_currency
FOREIGN KEY (credit_id, currency_id)
REFERENCES credit_currencies(credit_id, currency_id);

CREATE TABLE refinance_rates (
    id SERIAL PRIMARY KEY,
    valid_from_date DATE NOT NULL UNIQUE,
    valid_to_date DATE,
    rate_percent DECIMAL(5,2) NOT NULL
);

ALTER TABLE refinance_rates 
ADD CONSTRAINT chk_refinance_rates_rate 
CHECK (rate_percent >= 0);

ALTER TABLE refinance_rates
ADD CONSTRAINT chk_refinance_rates_date_order
CHECK (valid_to_date IS NULL OR valid_to_date >= valid_from_date);

CREATE TABLE penalties (
    id SERIAL PRIMARY KEY,
    credit_id INTEGER REFERENCES credits(id),
    penalty_type VARCHAR(20) NOT NULL,
    value_percent DECIMAL(6,4) NOT NULL,
    valid_from DATE NOT NULL
);

ALTER TABLE penalties 
ADD CONSTRAINT chk_penalties_type 
CHECK (penalty_type IN ('early_repayment', 'late_payment'));

ALTER TABLE penalties 
ADD CONSTRAINT chk_penalties_value 
CHECK (value_percent >= 0 AND value_percent < 100);

ALTER TABLE penalties
ADD CONSTRAINT uq_penalties_credit_type_valid_from
UNIQUE (penalty_type, valid_from);

CREATE TABLE contracts (
    id SERIAL PRIMARY KEY,
    client_id INTEGER REFERENCES clients(id),
    credit_id INTEGER REFERENCES credits(id),
    currency_id INTEGER REFERENCES currencies(id),
    interest_rate_id INTEGER REFERENCES interest_rates(id),
    contract_amount DECIMAL(15,2) NOT NULL,
    term_months INTEGER NOT NULL,
    issue_date DATE NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'Оформляется',
    rate_type VARCHAR(20) NOT NULL,
    fixed_interest_rate DECIMAL(10,4),
    fixed_additive_percent DECIMAL(6,4),
    fixed_early_penalty_x DECIMAL(6,4) DEFAULT 0,
    fixed_late_penalty_z DECIMAL(6,4),
    remaining_principal DECIMAL(15,2) NOT NULL DEFAULT 0
);

ALTER TABLE contracts 
ADD CONSTRAINT chk_contracts_amount 
CHECK (contract_amount > 0);

ALTER TABLE contracts 
ADD CONSTRAINT chk_contracts_term 
CHECK (term_months > 0);

ALTER TABLE contracts 
ADD CONSTRAINT chk_contracts_status 
CHECK (status IN ('Оформляется', 'Оформлен', 'Завершён'));

ALTER TABLE contracts 
ADD CONSTRAINT chk_contracts_early_penalty 
CHECK (fixed_early_penalty_x >= 0 AND fixed_early_penalty_x < 100);

ALTER TABLE contracts 
ADD CONSTRAINT chk_contracts_late_penalty 
CHECK (fixed_late_penalty_z >= 0 AND fixed_late_penalty_z < 100);

ALTER TABLE contracts 
ADD CONSTRAINT chk_contracts_additive_percent 
CHECK (fixed_additive_percent IS NULL OR (fixed_additive_percent >= 0 AND fixed_additive_percent < 100));

ALTER TABLE contracts 
ADD CONSTRAINT chk_contracts_remaining 
CHECK (remaining_principal >= 0);

CREATE TABLE payments (
    id SERIAL PRIMARY KEY,
    contract_id INTEGER REFERENCES contracts(id),
    payment_date DATE NOT NULL,
    planned_payment_date DATE NOT NULL,
    payment_type VARCHAR(20) NOT NULL,
    principal_amount DECIMAL(15,2) NOT NULL,
    interest_amount DECIMAL(15,2) NOT NULL,
    applied_annual_rate DECIMAL(10,4) NOT NULL,
    early_penalty DECIMAL(15,2) DEFAULT 0,
    late_penalty DECIMAL(15,2) DEFAULT 0,
    total_amount DECIMAL(15,2) NOT NULL,
    remaining_after_payment DECIMAL(15,2) NOT NULL
);

ALTER TABLE payments 
ADD CONSTRAINT chk_payments_type 
CHECK (payment_type IN ('monthly', 'early'));

ALTER TABLE payments 
ADD CONSTRAINT chk_payments_principal 
CHECK (principal_amount >= 0);

ALTER TABLE payments 
ADD CONSTRAINT chk_payments_interest 
CHECK (interest_amount >= 0);

ALTER TABLE payments
ADD CONSTRAINT chk_payments_applied_annual_rate
CHECK (applied_annual_rate >= 0);

ALTER TABLE payments 
ADD CONSTRAINT chk_payments_early_penalty 
CHECK (early_penalty >= 0);

ALTER TABLE payments 
ADD CONSTRAINT chk_payments_late_penalty 
CHECK (late_penalty >= 0);

ALTER TABLE payments 
ADD CONSTRAINT chk_payments_total 
CHECK (total_amount > 0);

ALTER TABLE payments 
ADD CONSTRAINT chk_payments_remaining 
CHECK (remaining_after_payment >= 0);

CREATE TABLE guarantors (
    id SERIAL PRIMARY KEY,
    contract_id INTEGER REFERENCES contracts(id),
    phys_person_id INTEGER REFERENCES phys_persons(client_id) ON DELETE CASCADE
);

ALTER TABLE guarantors
ADD CONSTRAINT guarantors_unique_contract_person
UNIQUE (contract_id, phys_person_id);

CREATE TABLE pledges (
    id SERIAL PRIMARY KEY,
    contract_id INTEGER REFERENCES contracts(id),
    currency_id INTEGER NOT NULL REFERENCES currencies(id),
    property_name VARCHAR(255) NOT NULL,
    estimated_value DECIMAL(15,2) NOT NULL,
    assessment_date DATE NOT NULL,
    property_type VARCHAR(20) NOT NULL
);

ALTER TABLE pledges 
ADD CONSTRAINT chk_pledges_value 
CHECK (estimated_value > 0);

ALTER TABLE pledges 
ADD CONSTRAINT chk_pledges_type 
CHECK (property_type IN ('real_estate', 'vehicle', 'equipment'));

CREATE TABLE rates_history (
    id SERIAL PRIMARY KEY,
    interest_rate_id INTEGER REFERENCES interest_rates(id),
    change_date TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    old_value DECIMAL(5,4),
    new_value DECIMAL(5,4) NOT NULL,
    old_term_from INTEGER,
    old_term_to INTEGER,
    new_term_from INTEGER,
    new_term_to INTEGER
);

CREATE TABLE penalties_history (
    id SERIAL PRIMARY KEY,
    penalty_id INTEGER REFERENCES penalties(id),
    change_date TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    old_value DECIMAL(6,4),
    new_value DECIMAL(6,4) NOT NULL
);
