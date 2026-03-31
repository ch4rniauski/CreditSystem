import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  ApiService,
  CreditCurrencyRow,
  CreditProductRow,
  CreditProductWriteDto,
  CurrencyRow,
  InterestRateRow,
  InterestRateWriteDto,
  PenaltyRow,
  PenaltyWriteDto,
} from '../../core/api.service';
import { positiveDecimalValidator } from '../../core/validators';

@Component({
  selector: 'app-credit-products',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './credit-products.page.html',
  styleUrls: ['./credit-products.page.scss'],
})
export default class CreditProductsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly products = signal<CreditProductRow[]>([]);
  readonly currencies = signal<CurrencyRow[]>([]);
  readonly selectedProductId = signal<number | null>(null);
  readonly ccRows = signal<CreditCurrencyRow[]>([]);
  readonly rateRows = signal<InterestRateRow[]>([]);
  readonly penRows = signal<PenaltyRow[]>([]);
  readonly error = signal<string | null>(null);

  readonly linkedCurrencyOptions = computed(() => {
    const codes = new Set(this.ccRows().map((r) => r.currencyCode));
    return this.currencies().filter((c) => codes.has(c.code));
  });

  readonly rateType = signal<'fixed' | 'floating'>('fixed');
  readonly isFixedRate = computed(() => this.rateType() === 'fixed');
  readonly isFloatingRate = computed(() => this.rateType() === 'floating');

  readonly productForm = this.fb.nonNullable.group({
    name: ['', Validators.required],
    description: [''],
    clientType: ['legal', Validators.required],
    minAmount: [1, [Validators.required, Validators.min(0.01)]],
    maxAmount: [1_000_000, Validators.required],
    minTermMonths: [1, [Validators.required, Validators.min(1)]],
    maxTermMonths: [120, Validators.required],
  });

  readonly ccForm = this.fb.nonNullable.group({
    currencyId: [0, Validators.min(1)],
  });

  readonly rateForm = this.fb.nonNullable.group({
    currencyId: [0, Validators.min(1)],
    termFromMonths: [1, Validators.min(1)],
    termToMonths: [12, Validators.min(1)],
    rateType: ['fixed', Validators.required],
    rateValue: [null as number | null],
    additivePercent: [null as number | null],
    validFrom: ['', Validators.required],
    validTo: [''],
  });

  readonly penForm = this.fb.nonNullable.group({
    penaltyType: ['early_repayment', Validators.required],
    valuePercent: [0, [Validators.required, Validators.min(0), positiveDecimalValidator()]],
    validFrom: ['', Validators.required],
  });

  ngOnInit() {
    this.reloadProducts();
    this.api.currencies().subscribe((c) => {
      this.currencies.set(c);
      const first = c[0]?.id ?? 0;
      this.ccForm.patchValue({ currencyId: first });
      this.rateForm.patchValue({ currencyId: first });
    });

    // Синхронизируем сигнал с полем rateType и чистим неактуальные поля
    this.rateForm.get('rateType')?.valueChanges.subscribe((value) => {
      const type = value === 'floating' ? 'floating' : 'fixed';
      this.rateType.set(type);

      if (type === 'fixed') {
        this.rateForm.patchValue({ additivePercent: null }, { emitEvent: false });
      } else {
        this.rateForm.patchValue({ rateValue: null }, { emitEvent: false });
      }
    });
  }

  reloadProducts() {
    this.api.creditProducts().subscribe({
      next: (p) => this.products.set(p),
      error: () => this.error.set('Ошибка загрузки продуктов'),
    });
  }

  selectProduct(p: CreditProductRow) {
    this.selectedProductId.set(p.id);
    this.productForm.patchValue({
      name: p.name,
      description: p.description ?? '',
      clientType: p.clientType,
      minAmount: p.minAmount,
      maxAmount: p.maxAmount,
      minTermMonths: p.minTermMonths,
      maxTermMonths: p.maxTermMonths,
    });
    this.reloadDetails();
  }

  clearProductSelection() {
    this.selectedProductId.set(null);
    this.productForm.reset({
      name: '',
      description: '',
      clientType: 'legal',
      minAmount: 1,
      maxAmount: 1_000_000,
      minTermMonths: 1,
      maxTermMonths: 120,
    });
    this.ccRows.set([]);
    this.rateRows.set([]);
    this.penRows.set([]);
  }

  reloadDetails() {
    const id = this.selectedProductId();
    if (id === null) return;
    this.api.creditCurrencies(id).subscribe((r) => this.ccRows.set(r));
    this.api.interestRates(id).subscribe((r) => this.rateRows.set(r));
    this.api.penalties(id).subscribe((r) => this.penRows.set(r));
  }

  saveProduct() {
    if (this.productForm.invalid) return;
    const v = this.productForm.getRawValue() as CreditProductWriteDto;
    const id = this.selectedProductId();
    if (id === null) {
      this.api.createCreditProduct(v).subscribe({
        next: () => {
          this.reloadProducts();
          this.error.set(null);
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    } else {
      this.api.updateCreditProduct(id, v).subscribe({
        next: () => {
          this.reloadProducts();
          this.reloadDetails();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    }
  }

  deleteProduct(p: CreditProductRow) {
    if (!confirm(`Удалить продукт «${p.name}»?`)) return;
    this.api.deleteCreditProduct(p.id).subscribe({
      next: () => {
        if (this.selectedProductId() === p.id) this.clearProductSelection();
        this.reloadProducts();
      },
      error: (e) => this.error.set(typeof e.error === 'string' ? e.error : 'Невозможно удалить'),
    });
  }

  currencyIdByCode(code: string): number | null {
    return this.currencies().find((c) => c.code === code)?.id ?? null;
  }

  addCc() {
    const pid = this.selectedProductId();
    if (pid === null || this.ccForm.invalid) return;
    const v = this.ccForm.getRawValue();
    this.api.addCreditCurrency(pid, v).subscribe({
      next: () => {
        this.reloadDetails();
        this.error.set(null);
      },
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  updateCc(currencyCode: string) {
    // больше не редактируем базовую ставку, только удалять валютах продукта
  }

  removeCc(currencyCode: string) {
    const pid = this.selectedProductId();
    const cid = this.currencyIdByCode(currencyCode);
    if (pid === null || cid === null) return;
    this.api.deleteCreditCurrency(pid, cid).subscribe({
      next: () => this.reloadDetails(),
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  addRate() {
    const pid = this.selectedProductId();
    if (pid === null || this.rateForm.invalid) return;
    const v = this.rateForm.getRawValue();

    const body: InterestRateWriteDto = {
      creditId: pid,
      currencyId: Number(v.currencyId),
      termFromMonths: v.termFromMonths,
      termToMonths: v.termToMonths,
      rateType: v.rateType,
      rateValue: v.rateType === 'fixed' ? v.rateValue : null,
      additivePercent: v.rateType === 'floating' ? v.additivePercent : null,
      validFrom: v.validFrom,
      validTo: v.validTo || null,
    };
    this.api.createInterestRate(body).subscribe({
      next: () => {
        this.rateForm.reset({
          currencyId: 0,
          termFromMonths: 1,
          termToMonths: 12,
          rateType: 'fixed',
          rateValue: null,
          additivePercent: null,
          validFrom: '',
          validTo: '',
        });
        this.reloadDetails();
        this.error.set(null);
      },
      error: (e) => this.error.set(e.error?.title ?? e.error ?? 'Ошибка'),
    });
  }

  deleteRate(r: InterestRateRow) {
    this.api.deleteInterestRate(r.id).subscribe({
      next: () => this.reloadDetails(),
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  addPenalty() {
    const pid = this.selectedProductId();
    if (pid === null || this.penForm.invalid) return;
    const v = this.penForm.getRawValue();
    const body: PenaltyWriteDto = {
      creditId: pid,
      penaltyType: v.penaltyType,
      valuePercent: v.valuePercent,
      validFrom: v.validFrom,
    };
    this.api.createPenalty(body).subscribe({
      next: () => this.reloadDetails(),
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  deletePenalty(p: PenaltyRow) {
    this.api.deletePenalty(p.id).subscribe({
      next: () => this.reloadDetails(),
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }
}
