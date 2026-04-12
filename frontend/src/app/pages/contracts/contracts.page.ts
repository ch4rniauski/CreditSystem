import { CommonModule, DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, ContractDetailsDto, ContractRow, CreditProductRow, CurrencyRow } from '../../core/api.service';

@Component({
  selector: 'app-contracts',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './contracts.page.html',
  styleUrls: ['./contracts.page.scss'],
  host: {
    '(document:keydown.escape)': 'onEscape()',
  },
})
export default class ContractsPage implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  private readonly document = inject(DOCUMENT);

  readonly rows = signal<ContractRow[]>([]);
  readonly credits = signal<CreditProductRow[]>([]);
  readonly currencies = signal<CurrencyRow[]>([]);
  readonly availableCurrencies = signal<CurrencyRow[]>([]);
  readonly clientOptions = signal<{ id: number; kind: string; label: string }[]>([]);
  readonly viewedContract = signal<ContractDetailsDto | null>(null);
  readonly detailsLoading = signal(false);
  readonly signCandidate = signal<ContractDetailsDto | null>(null);
  readonly signLoading = signal(false);
  readonly signing = signal(false);
  readonly editingContractId = signal<number | null>(null);
  readonly error = signal<string | null>(null);

  private readonly syncBodyScrollLock = effect(() => {
    const hasOpenModal =
      this.detailsLoading() || !!this.viewedContract() || this.signLoading() || !!this.signCandidate();
    this.document.body.classList.toggle('modal-open', hasOpenModal);
  });

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
      if (creditId) {
        this.loadCreditConstraints(Number(creditId));
      }
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
        if (first) {
          this.form.patchValue({ clientId: first });
        }
      });
    });
  }

  ngOnDestroy() {
    this.syncBodyScrollLock.destroy();
    this.document.body.classList.remove('modal-open');
  }

  reload() {
    this.api.contracts().subscribe({
      next: (r) => this.rows.set(r),
      error: () => this.error.set('Ошибка загрузки'),
    });
  }

  private loadCreditConstraints(creditId: number, preferredCurrencyId?: number) {
    const credit = this.credits().find((c) => c.id === creditId);
    if (!credit) {
      return;
    }

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

      const targetCurrencyId = preferredCurrencyId ?? this.form.controls.currencyId.value;
      if (!available.some((c) => c.id === targetCurrencyId)) {
        const first = available[0]?.id ?? this.currencies()[0]?.id ?? 0;
        if (first) {
          this.form.patchValue({ currencyId: first });
        }
      } else if (targetCurrencyId) {
        this.form.patchValue({ currencyId: targetCurrencyId });
      }
    });
  }

  create() {
    if (this.form.invalid) {
      return;
    }

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

    const payload = {
      clientId: Number(v.clientId),
      creditId: Number(v.creditId),
      currencyId: Number(v.currencyId),
      contractAmount: v.contractAmount,
      termMonths: v.termMonths,
      issueDate: v.issueDate,
    };
    const editingId = this.editingContractId();
    const request$ = editingId === null
      ? this.api.createContract(payload)
      : this.api.updateContract(editingId, payload);

    request$.subscribe({
      next: () => {
        this.reload();
        this.cancelEdit();
        this.error.set(null);
      },
      error: (e) =>
        this.error.set(typeof e.error === 'string' ? e.error : JSON.stringify(e.error?.errors ?? e.error)),
    });
  }

  edit(c: ContractRow) {
    this.api.contractDetails(c.id).subscribe({
      next: (d) => {
        this.editingContractId.set(c.id);
        this.form.patchValue({
          clientId: d.clientId,
          creditId: d.creditId,
          contractAmount: d.contractAmount,
          termMonths: d.termMonths,
          issueDate: d.issueDate,
        });
        this.loadCreditConstraints(d.creditId, d.currencyId);
        this.error.set(null);
      },
      error: (e) => {
        this.error.set(typeof e.error === 'string' ? e.error : 'Не удалось загрузить договор для редактирования');
      },
    });
  }

  cancelEdit() {
    this.editingContractId.set(null);
  }

  sign(c: ContractRow) {
    this.signLoading.set(true);
    this.api.contractDetails(c.id).subscribe({
      next: (details) => {
        this.signCandidate.set(details);
        this.signLoading.set(false);
      },
      error: (e) => {
        this.signLoading.set(false);
        this.error.set(typeof e.error === 'string' ? e.error : 'Не удалось загрузить финальные условия договора');
      },
    });
  }

  confirmSign() {
    const candidate = this.signCandidate();
    if (!candidate) {
      return;
    }

    this.signing.set(true);
    this.api.signContract(candidate.id).subscribe({
      next: () => {
        this.signing.set(false);
        this.closeSignModal();
        this.reload();
      },
      error: (e) => {
        this.signing.set(false);
        this.error.set(typeof e.error === 'string' ? e.error : 'Ошибка');
      },
    });
  }

  closeSignModal() {
    this.signCandidate.set(null);
    this.signLoading.set(false);
    this.signing.set(false);
  }

  remove(c: ContractRow) {
    if (!confirm('Удалить черновик?')) {
      return;
    }

    this.api.deleteContract(c.id).subscribe({
      next: () => {
        this.error.set(null);
        this.reload();
      },
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

  onEscape() {
    if (this.signCandidate() || this.signLoading()) {
      this.closeSignModal();
      return;
    }

    if (this.viewedContract() || this.detailsLoading()) {
      this.closeDetails();
    }
  }
  contractRateSummary(d: ContractDetailsDto): string {
    if (d.rateType === 'floating') {
      const additive = d.fixedAdditivePercent ?? 0;
      return `Плавающая (надбавка ${additive}%)`;
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

  pledgeTypeLabel(value: string): string {
    const labels: { [key: string]: string } = {
      real_estate: 'Недвижимость',
      vehicle: 'Автотранспорт',
      equipment: 'Оборудование',
    };

    return labels[value] ?? value;
  }
}
