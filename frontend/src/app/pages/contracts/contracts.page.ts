import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, ContractDetailsDto, ContractRow, CreditProductRow, CurrencyRow } from '../../core/api.service';

@Component({
  selector: 'app-contracts',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './contracts.page.html',
  styleUrls: ['./contracts.page.scss'],
})
export default class ContractsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly rows = signal<ContractRow[]>([]);
  readonly credits = signal<CreditProductRow[]>([]);
  readonly currencies = signal<CurrencyRow[]>([]);
  readonly availableCurrencies = signal<CurrencyRow[]>([]);
  readonly clientOptions = signal<{ id: number; kind: string; label: string }[]>([]);
  readonly viewedContract = signal<ContractDetailsDto | null>(null);
  readonly detailsLoading = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    clientId: [0, Validators.min(1)],
    creditId: [0, Validators.min(1)],
    currencyId: [0, Validators.min(1)],
    contractAmount: [1000, [Validators.required, Validators.min(0.01)]],
    termMonths: [12, [Validators.required, Validators.min(1)]],
    issueDate: ['', Validators.required],
  });

  ngOnInit() {
    this.reload();

    this.api.creditProducts().subscribe((c) => {
      this.credits.set(c);
      const first = c[0]?.id ?? 0;
      if (first) {
        this.form.patchValue({ creditId: first });
        this.loadCreditConstraints(first);
      }
    });

    this.api.currencies().subscribe((c) => {
      this.currencies.set(c);
      const first = c[0]?.id ?? 0;
      if (first) {
        this.form.patchValue({ currencyId: first });
        this.availableCurrencies.set(c);
      }
    });

    this.form.get('creditId')?.valueChanges.subscribe((creditId) => {
      if (creditId) this.loadCreditConstraints(Number(creditId));
    });

    this.api.legalClients().subscribe((legals) => {
      this.api.physicalClients().subscribe((phys) => {
        const opts = [
          ...legals.map((l) => ({ id: l.clientId, kind: 'ЮЛ', label: l.name })),
          ...phys.map((p) => ({ id: p.clientId, kind: 'ФЛ', label: p.fullName })),
        ];
        const sorted = opts.sort((a, b) => a.label.localeCompare(b.label));
        this.clientOptions.set(sorted);
        const first = sorted[0]?.id ?? 0;
        if (first) this.form.patchValue({ clientId: first });
      });
    });
  }

  reload() {
    this.api.contracts().subscribe({
      next: (r) => this.rows.set(r),
      error: () => this.error.set('Ошибка загрузки'),
    });
  }

  private loadCreditConstraints(creditId: number) {
    const credit = this.credits().find((c) => c.id === creditId);
    if (!credit) return;

    this.form.controls.contractAmount.setValidators([
      Validators.required,
      Validators.min(credit.minAmount),
      Validators.max(credit.maxAmount),
    ]);
    this.form.controls.termMonths.setValidators([
      Validators.required,
      Validators.min(credit.minTermMonths),
      Validators.max(credit.maxTermMonths),
    ]);
    this.form.controls.contractAmount.updateValueAndValidity({ emitEvent: false });
    this.form.controls.termMonths.updateValueAndValidity({ emitEvent: false });

    this.form.patchValue({
      contractAmount: credit.minAmount,
      termMonths: credit.minTermMonths,
    });

    this.api.creditCurrencies(creditId).subscribe((rows) => {
      const matchedIds = rows
        .map((r) => this.currencies().find((c) => c.code === r.currencyCode)?.id)
        .filter((id): id is number => !!id);
      const available = this.currencies().filter((c) => matchedIds.includes(c.id));
      this.availableCurrencies.set(available.length ? available : this.currencies());

      if (!available.some((c) => c.id === this.form.controls.currencyId.value)) {
        const first = available[0]?.id ?? this.currencies()[0]?.id ?? 0;
        if (first) this.form.patchValue({ currencyId: first });
      }
    });
  }

  create() {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();

    const credit = this.credits().find((c) => c.id === Number(v.creditId));
    if (!credit) {
      this.error.set('Выбранный кредитный продукт не найден.');
      return;
    }

    if (v.termMonths < credit.minTermMonths || v.termMonths > credit.maxTermMonths) {
      this.error.set(`Срок должен быть от ${credit.minTermMonths} до ${credit.maxTermMonths} месяцев.`);
      return;
    }

    if (!this.availableCurrencies().some((c) => c.id === Number(v.currencyId))) {
      this.error.set('Выбранная валюта не доступна для этого кредитного продукта.');
      return;
    }

    this.api
      .createContract({
        clientId: Number(v.clientId),
        creditId: Number(v.creditId),
        currencyId: Number(v.currencyId),
        contractAmount: v.contractAmount,
        termMonths: v.termMonths,
        issueDate: v.issueDate,
      })
      .subscribe({
        next: () => {
          this.reload();
          this.error.set(null);
        },
        error: (e) =>
          this.error.set(typeof e.error === 'string' ? e.error : JSON.stringify(e.error?.errors ?? e.error)),
      });
  }

  sign(c: ContractRow) {
    if (!confirm(`Оформить договор «${c.creditName}» для ${c.clientDisplay}?`)) return;
    this.api.signContract(c.id).subscribe({
      next: () => this.reload(),
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  remove(c: ContractRow) {
    if (!confirm('Удалить черновик?')) return;
    this.api.deleteContract(c.id).subscribe({
      next: () => this.reload(),
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  openDetails(c: ContractRow) {
    this.detailsLoading.set(true);
    this.api.contractDetails(c.id).subscribe({
      next: (details) => {
        this.viewedContract.set(details);
        this.detailsLoading.set(false);
      },
      error: (e) => {
        this.detailsLoading.set(false);
        this.error.set(typeof e.error === 'string' ? e.error : 'Не удалось загрузить условия договора');
      },
    });
  }

  closeDetails() {
    this.viewedContract.set(null);
    this.detailsLoading.set(false);
  }

  rateTypeLabel(value: string): string {
    return value === 'floating' ? 'Плавающая' : 'Фиксированная';
  }

  contractRateSummary(d: ContractDetailsDto): string {
    if (d.rateType === 'floating') {
      const additive = d.fixedAdditivePercent ?? 0;
      const annual = d.fixedInterestRate ?? 0;
      return `Плавающая (${annual}%)`;
    }

    return `Фиксированная (${d.fixedInterestRate ?? 0}%)`;
  }

  clientDisplayWithPassport(d: ContractDetailsDto): string {
    if (d.clientPassportSeries && d.clientPassportNumber) {
      return `${d.clientDisplay} (${d.clientPassportSeries}${d.clientPassportNumber})`;
    }

    return d.clientDisplay;
  }

  clientTypeLabel(value: string): string {
    return value === 'legal' ? 'Юридическое лицо' : 'Физическое лицо';
  }
}
