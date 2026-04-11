using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace CreditSystem.Database;

public partial class CreditSystemContext : DbContext
{
    public CreditSystemContext()
    {
    }

    public CreditSystemContext(DbContextOptions<CreditSystemContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Client> Clients { get; set; }

    public virtual DbSet<Contract> Contracts { get; set; }

    public virtual DbSet<Credit> Credits { get; set; }

    public virtual DbSet<CreditCurrency> CreditCurrencies { get; set; }

    public virtual DbSet<CreditHistoryReport> CreditHistoryReports { get; set; }

    public virtual DbSet<Currency> Currencies { get; set; }

    public virtual DbSet<CurrentDebtReport> CurrentDebtReports { get; set; }

    public virtual DbSet<ExpectedPayment> ExpectedPayments { get; set; }

    public virtual DbSet<Guarantor> Guarantors { get; set; }

    public virtual DbSet<InterestRate> InterestRates { get; set; }

    public virtual DbSet<LegalPerson> LegalPersons { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<PaymentCalendar> PaymentCalendars { get; set; }

    public virtual DbSet<PenaltiesHistory> PenaltiesHistories { get; set; }

    public virtual DbSet<Penalty> Penalties { get; set; }

    public virtual DbSet<PhysPerson> PhysPersons { get; set; }

    public virtual DbSet<Pledge> Pledges { get; set; }

    public virtual DbSet<RatesHistory> RatesHistories { get; set; }

    public virtual DbSet<RefinanceRate> RefinanceRates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("clients_pkey");

            entity.ToTable("clients");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ClientType)
                .HasMaxLength(20)
                .HasColumnName("client_type");
        });

        modelBuilder.Entity<Contract>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("contracts_pkey");

            entity.ToTable("contracts");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ClientId).HasColumnName("client_id");
            entity.Property(e => e.ContractAmount)
                .HasPrecision(15, 2)
                .HasColumnName("contract_amount");
            entity.Property(e => e.CreditId).HasColumnName("credit_id");
            entity.Property(e => e.CurrencyId).HasColumnName("currency_id");
            entity.Property(e => e.FixedAdditivePercent)
                .HasPrecision(6, 4)
                .HasColumnName("fixed_additive_percent");
            entity.Property(e => e.FixedEarlyPenaltyX)
                .HasPrecision(6, 4)
                .HasDefaultValue(0m)
                .HasColumnName("fixed_early_penalty_x");
            entity.Property(e => e.FixedInterestRate)
                .HasPrecision(10, 4)
                .HasColumnName("fixed_interest_rate");
            entity.Property(e => e.FixedLatePenaltyZ)
                .HasPrecision(6, 4)
                .HasColumnName("fixed_late_penalty_z");
            entity.Property(e => e.InterestRateId).HasColumnName("interest_rate_id");
            entity.Property(e => e.IssueDate).HasColumnName("issue_date");
            entity.Property(e => e.RateType)
                .HasMaxLength(20)
                .HasColumnName("rate_type");
            entity.Property(e => e.RemainingPrincipal)
                .HasPrecision(15, 2)
                .HasColumnName("remaining_principal");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Оформляется'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TermMonths).HasColumnName("term_months");

            entity.HasOne(d => d.Client).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("contracts_client_id_fkey");

            entity.HasOne(d => d.Credit).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.CreditId)
                .HasConstraintName("contracts_credit_id_fkey");

            entity.HasOne(d => d.Currency).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.CurrencyId)
                .HasConstraintName("contracts_currency_id_fkey");

            entity.HasOne(d => d.InterestRate).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.InterestRateId)
                .HasConstraintName("contracts_interest_rate_id_fkey");
        });

        modelBuilder.Entity<Credit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("credits_pkey");

            entity.ToTable("credits");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ClientType)
                .HasMaxLength(20)
                .HasColumnName("client_type");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.MaxAmount)
                .HasPrecision(15, 2)
                .HasColumnName("max_amount");
            entity.Property(e => e.MaxTermMonths).HasColumnName("max_term_months");
            entity.Property(e => e.MinAmount)
                .HasPrecision(15, 2)
                .HasColumnName("min_amount");
            entity.Property(e => e.MinTermMonths).HasColumnName("min_term_months");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
        });

        modelBuilder.Entity<CreditCurrency>(entity =>
        {
            entity.HasKey(e => new { e.CreditId, e.CurrencyId }).HasName("credit_currencies_pkey");

            entity.ToTable("credit_currencies");

            entity.Property(e => e.CreditId).HasColumnName("credit_id");
            entity.Property(e => e.CurrencyId).HasColumnName("currency_id");

            entity.HasOne(d => d.Credit).WithMany(p => p.CreditCurrencies)
                .HasForeignKey(d => d.CreditId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("credit_currencies_credit_id_fkey");

            entity.HasOne(d => d.Currency).WithMany(p => p.CreditCurrencies)
                .HasForeignKey(d => d.CurrencyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("credit_currencies_currency_id_fkey");
        });

        modelBuilder.Entity<CreditHistoryReport>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("credit_history_report");

            entity.Property(e => e.ChangeDate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("change_date");
            entity.Property(e => e.NewTermFrom).HasColumnName("new_term_from");
            entity.Property(e => e.NewTermTo).HasColumnName("new_term_to");
            entity.Property(e => e.NewValue)
                .HasPrecision(5, 4)
                .HasColumnName("new_value");
            entity.Property(e => e.OldTermFrom).HasColumnName("old_term_from");
            entity.Property(e => e.OldTermTo).HasColumnName("old_term_to");
            entity.Property(e => e.OldValue)
                .HasPrecision(5, 4)
                .HasColumnName("old_value");
        });

        modelBuilder.Entity<Currency>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("currencies_pkey");

            entity.ToTable("currencies");

            entity.HasIndex(e => e.Code, "currencies_code_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(3)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<CurrentDebtReport>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("current_debt_report");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.InterestDue).HasColumnName("interest_due");
            entity.Property(e => e.Penalties).HasColumnName("penalties");
            entity.Property(e => e.RemainingPrincipal)
                .HasPrecision(15, 2)
                .HasColumnName("remaining_principal");
        });

        modelBuilder.Entity<ExpectedPayment>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("expected_payments");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MonthlyPrincipal).HasColumnName("monthly_principal");
        });

        modelBuilder.Entity<Guarantor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("guarantors_pkey");

            entity.ToTable("guarantors");

            entity.HasIndex(e => new { e.ContractId, e.PhysPersonId }, "guarantors_unique_contract_person").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ContractId).HasColumnName("contract_id");
            entity.Property(e => e.PhysPersonId).HasColumnName("phys_person_id");

            entity.HasOne(d => d.Contract).WithMany(p => p.Guarantors)
                .HasForeignKey(d => d.ContractId)
                .HasConstraintName("guarantors_contract_id_fkey");

            entity.HasOne(d => d.PhysPerson).WithMany(p => p.Guarantors)
                .HasForeignKey(d => d.PhysPersonId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("guarantors_phys_person_id_fkey");
        });

        modelBuilder.Entity<InterestRate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("interest_rates_pkey");

            entity.ToTable("interest_rates");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AdditivePercent)
                .HasPrecision(5, 4)
                .HasColumnName("additive_percent");
            entity.Property(e => e.CreditId).HasColumnName("credit_id");
            entity.Property(e => e.CurrencyId).HasColumnName("currency_id");
            entity.Property(e => e.RateType)
                .HasMaxLength(20)
                .HasColumnName("rate_type");
            entity.Property(e => e.RateValue)
                .HasPrecision(5, 4)
                .HasColumnName("rate_value");
            entity.Property(e => e.TermFromMonths).HasColumnName("term_from_months");
            entity.Property(e => e.TermToMonths).HasColumnName("term_to_months");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to");

            entity.HasOne(d => d.Credit).WithMany(p => p.InterestRates)
                .HasForeignKey(d => d.CreditId)
                .HasConstraintName("interest_rates_credit_id_fkey");

            entity.HasOne(d => d.Currency).WithMany(p => p.InterestRates)
                .HasForeignKey(d => d.CurrencyId)
                .HasConstraintName("interest_rates_currency_id_fkey");

            entity.HasOne(d => d.CreditCurrency).WithMany(p => p.InterestRates)
                .HasForeignKey(d => new { d.CreditId, d.CurrencyId })
                .HasConstraintName("fk_interest_rates_credit_currency");
        });

        modelBuilder.Entity<LegalPerson>(entity =>
        {
            entity.HasKey(e => e.ClientId).HasName("legal_persons_pkey");

            entity.ToTable("legal_persons");

            entity.Property(e => e.ClientId)
                .ValueGeneratedNever()
                .HasColumnName("client_id");
            entity.Property(e => e.LegalAddress).HasColumnName("legal_address");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.OwnershipType)
                .HasMaxLength(50)
                .HasColumnName("ownership_type");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");

            entity.HasOne(d => d.Client).WithOne(p => p.LegalPerson)
                .HasForeignKey<LegalPerson>(d => d.ClientId)
                .HasConstraintName("legal_persons_client_id_fkey");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("payments_pkey");

            entity.ToTable("payments");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppliedAnnualRate)
                .HasPrecision(10, 4)
                .HasColumnName("applied_annual_rate");
            entity.Property(e => e.ContractId).HasColumnName("contract_id");
            entity.Property(e => e.EarlyPenalty)
                .HasPrecision(15, 2)
                .HasDefaultValue(0m)
                .HasColumnName("early_penalty");
            entity.Property(e => e.InterestAmount)
                .HasPrecision(15, 2)
                .HasColumnName("interest_amount");
            entity.Property(e => e.LatePenalty)
                .HasPrecision(15, 2)
                .HasDefaultValue(0m)
                .HasColumnName("late_penalty");
            entity.Property(e => e.PaymentDate).HasColumnName("payment_date");
            entity.Property(e => e.PaymentType)
                .HasMaxLength(20)
                .HasColumnName("payment_type");
            entity.Property(e => e.PlannedPaymentDate).HasColumnName("planned_payment_date");
            entity.Property(e => e.PrincipalAmount)
                .HasPrecision(15, 2)
                .HasColumnName("principal_amount");
            entity.Property(e => e.RemainingAfterPayment)
                .HasPrecision(15, 2)
                .HasColumnName("remaining_after_payment");
            entity.Property(e => e.TotalAmount)
                .HasPrecision(15, 2)
                .HasColumnName("total_amount");

            entity.HasOne(d => d.Contract).WithMany(p => p.Payments)
                .HasForeignKey(d => d.ContractId)
                .HasConstraintName("payments_contract_id_fkey");
        });

        modelBuilder.Entity<PaymentCalendar>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("payment_calendar");

            entity.Property(e => e.ContractId).HasColumnName("contract_id");
            entity.Property(e => e.PlannedDate).HasColumnName("planned_date");
        });

        modelBuilder.Entity<PenaltiesHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("penalties_history_pkey");

            entity.ToTable("penalties_history");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChangeDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("change_date");
            entity.Property(e => e.NewValue)
                .HasPrecision(6, 4)
                .HasColumnName("new_value");
            entity.Property(e => e.OldValue)
                .HasPrecision(6, 4)
                .HasColumnName("old_value");
            entity.Property(e => e.PenaltyId).HasColumnName("penalty_id");

            entity.HasOne(d => d.Penalty).WithMany(p => p.PenaltiesHistories)
                .HasForeignKey(d => d.PenaltyId)
                .HasConstraintName("penalties_history_penalty_id_fkey");
        });

        modelBuilder.Entity<Penalty>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("penalties_pkey");

            entity.ToTable("penalties");

            entity.HasIndex(e => new { e.PenaltyType, e.ValidFrom }, "uq_penalties_credit_type_valid_from").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreditId).HasColumnName("credit_id");
            entity.Property(e => e.PenaltyType)
                .HasMaxLength(20)
                .HasColumnName("penalty_type");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from");
            entity.Property(e => e.ValuePercent)
                .HasPrecision(6, 4)
                .HasColumnName("value_percent");

            entity.HasOne(d => d.Credit).WithMany(p => p.Penalties)
                .HasForeignKey(d => d.CreditId)
                .HasConstraintName("penalties_credit_id_fkey");
        });

        modelBuilder.Entity<PhysPerson>(entity =>
        {
            entity.HasKey(e => e.ClientId).HasName("phys_persons_pkey");

            entity.ToTable("phys_persons");

            entity.HasIndex(e => new { e.PassportSeries, e.PassportNumber }, "uq_phys_persons_passport").IsUnique();

            entity.HasIndex(e => e.Phone, "uq_phys_persons_phone").IsUnique();

            entity.Property(e => e.ClientId)
                .ValueGeneratedNever()
                .HasColumnName("client_id");
            entity.Property(e => e.ActualAddress).HasColumnName("actual_address");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.PassportNumber)
                .HasMaxLength(7)
                .HasColumnName("passport_number");
            entity.Property(e => e.PassportSeries)
                .HasMaxLength(2)
                .HasColumnName("passport_series");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");

            entity.HasOne(d => d.Client).WithOne(p => p.PhysPerson)
                .HasForeignKey<PhysPerson>(d => d.ClientId)
                .HasConstraintName("phys_persons_client_id_fkey");
        });

        modelBuilder.Entity<Pledge>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pledges_pkey");

            entity.ToTable("pledges");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AssessmentDate).HasColumnName("assessment_date");
            entity.Property(e => e.ContractId).HasColumnName("contract_id");
            entity.Property(e => e.CurrencyId).HasColumnName("currency_id");
            entity.Property(e => e.EstimatedValue)
                .HasPrecision(15, 2)
                .HasColumnName("estimated_value");
            entity.Property(e => e.PropertyName)
                .HasMaxLength(255)
                .HasColumnName("property_name");
            entity.Property(e => e.PropertyType)
                .HasMaxLength(20)
                .HasColumnName("property_type");

            entity.HasOne(d => d.Contract).WithMany(p => p.Pledges)
                .HasForeignKey(d => d.ContractId)
                .HasConstraintName("pledges_contract_id_fkey");

            entity.HasOne(d => d.Currency).WithMany(p => p.Pledges)
                .HasForeignKey(d => d.CurrencyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("pledges_currency_id_fkey");
        });

        modelBuilder.Entity<RatesHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("rates_history_pkey");

            entity.ToTable("rates_history");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChangeDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("change_date");
            entity.Property(e => e.InterestRateId).HasColumnName("interest_rate_id");
            entity.Property(e => e.NewTermFrom).HasColumnName("new_term_from");
            entity.Property(e => e.NewTermTo).HasColumnName("new_term_to");
            entity.Property(e => e.NewValue)
                .HasPrecision(5, 4)
                .HasColumnName("new_value");
            entity.Property(e => e.OldTermFrom).HasColumnName("old_term_from");
            entity.Property(e => e.OldTermTo).HasColumnName("old_term_to");
            entity.Property(e => e.OldValue)
                .HasPrecision(5, 4)
                .HasColumnName("old_value");

            entity.HasOne(d => d.InterestRate).WithMany(p => p.RatesHistories)
                .HasForeignKey(d => d.InterestRateId)
                .HasConstraintName("rates_history_interest_rate_id_fkey");
        });

        modelBuilder.Entity<RefinanceRate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("refinance_rates_pkey");

            entity.ToTable("refinance_rates");

            entity.HasIndex(e => e.ValidFromDate, "refinance_rates_valid_from_date_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RatePercent)
                .HasPrecision(5, 2)
                .HasColumnName("rate_percent");
            entity.Property(e => e.ValidFromDate).HasColumnName("valid_from_date");
            entity.Property(e => e.ValidToDate).HasColumnName("valid_to_date");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
