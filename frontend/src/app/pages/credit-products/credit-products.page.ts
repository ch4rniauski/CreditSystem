import {CommonModule} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject, OnInit, signal} from '@angular/core';
import {FormBuilder, ReactiveFormsModule, Validators} from '@angular/forms';
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
import {nonNegativeValidator} from '../../core/validators';

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
  readonly editingRateId = signal<number | null>(null);
  readonly editingPenaltyId = signal<number | null>(null);

  // Section-specific errors
  readonly productError = signal<string | null>(null);
  readonly currenciesError = signal<string | null>(null);
  readonly interestRatesError = signal<string | null>(null);
  readonly penaltiesError = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  readonly linkedCurrencyOptions = computed(() => {
    const codes = new Set(this.ccRows().map((r) => r.currencyCode));
    return this.currencies().filter((c) => codes.has(c.code));
  });

  readonly rateType = signal<'fixed' | 'floating'>('fixed');
  readonly isFixedRate = computed(() => this.rateType() === 'fixed');
  readonly isFloatingRate = computed(() => this.rateType() === 'floating');
  private readonly rateTermRangeError = 'Срок "от" не может быть больше срока "до"';
  private readonly rateTermBoundsError = 'Сроки процентной ставки должны быть в пределах минимального и максимального срока кредитного продукта';

  private selectedProduct(): CreditProductRow | null {
    const id = this.selectedProductId();
    if (id === null) {
      return null;
    }

    return this.products().find((p) => p.id === id) ?? null;
  }

  rateTermLimitsLabel(): string {
    const product = this.selectedProduct();
    if (!product) {
      return 'Срок в месяцах';
    }

    return `Срок в месяцах (${product.minTermMonths}-${product.maxTermMonths})`;
  }

  private isRateTermWithinProductBounds(termFrom: number, termTo: number): boolean {
    const product = this.selectedProduct();
    if (!product) {
      return false;
    }

    return termFrom >= product.minTermMonths
      && termFrom <= product.maxTermMonths
      && termTo >= product.minTermMonths
      && termTo <= product.maxTermMonths;
  }

  private areRateRequiredFieldsFilled(): boolean {
    const v = this.rateForm.getRawValue();
    if (Number(v.currencyId) < 1) {
      return false;
    }

    if (!v.validFrom) {
      return false;
    }

    if (v.termFromMonths < 1 || v.termToMonths < 1) {
      return false;
    }

    if (v.rateType === 'fixed') {
      return v.rateValue !== null;
    }

    return v.additivePercent !== null;
  }

  private refreshRateTermsValidationMessage() {
    const currentError = this.interestRatesError();
    const isOwnTermsError = currentError === this.rateTermRangeError || currentError === this.rateTermBoundsError;

    if (!this.areRateRequiredFieldsFilled()) {
      if (isOwnTermsError) {
        this.interestRatesError.set(null);
      }

      return;
    }

    const v = this.rateForm.getRawValue();
    if (v.termFromMonths > v.termToMonths) {
      this.interestRatesError.set(this.rateTermRangeError);
      return;
    }

    if (!this.isRateTermWithinProductBounds(v.termFromMonths, v.termToMonths)) {
      this.interestRatesError.set(this.rateTermBoundsError);
      return;
    }

    if (isOwnTermsError) {
      this.interestRatesError.set(null);
    }
  }

  canAddRate(): boolean {
    if (this.selectedProductId() === null) {
      return false;
    }

    const v = this.rateForm.getRawValue();
    if (Number(v.currencyId) < 1) {
      return false;
    }

    if (!v.validFrom) {
      return false;
    }

    if (v.termFromMonths < 1 || v.termToMonths < 1) {
      return false;
    }

    if (v.termFromMonths > v.termToMonths) {
      return false;
    }

    if (!this.isRateTermWithinProductBounds(v.termFromMonths, v.termToMonths)) {
      return false;
    }

    if (v.validTo && new Date(v.validFrom) > new Date(v.validTo)) {
      return false;
    }

    if (v.rateType === 'fixed') {
      return v.rateValue !== null && Number(v.rateValue) >= 0 && Number(v.rateValue) <= 9.9999;
    }

    return (
      v.additivePercent !== null
      && Number(v.additivePercent) >= 0
      && Number(v.additivePercent) <= 9.9999
    );
  }

  readonly productForm = this.fb.nonNullable.group({
    name: ['', Validators.required],
    description: [''],
    clientType: ['legal', Validators.required],
    minAmount: [1, [Validators.required, Validators.min(0.01), nonNegativeValidator()]],
    maxAmount: [1_000_000, [Validators.required, nonNegativeValidator()]],
    minTermMonths: [1, [Validators.required, Validators.min(1)]],
    maxTermMonths: [120, [Validators.required, Validators.min(1)]],
  });

  readonly ccForm = this.fb.nonNullable.group({
    currencyId: [0, Validators.min(1)],
  });

  readonly rateForm = this.fb.nonNullable.group({
    currencyId: [0, Validators.min(1)],
    termFromMonths: [1, [Validators.required, Validators.min(1)]],
    termToMonths: [12, [Validators.required, Validators.min(1)]],
    rateType: ['fixed', Validators.required],
    rateValue: [null as number | null, nonNegativeValidator()],
    additivePercent: [null as number | null, nonNegativeValidator()],
    validFrom: ['', Validators.required],
    validTo: [''],
  });

  readonly penForm = this.fb.nonNullable.group({
    penaltyType: ['early_repayment', Validators.required],
    valuePercent: [0, [Validators.required, Validators.min(0), Validators.max(99.9999), nonNegativeValidator()]],
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

      this.applyRateTypeValidators(type);
      this.refreshRateTermsValidationMessage();
    });

    this.rateForm.valueChanges.subscribe(() => this.refreshRateTermsValidationMessage());

    this.applyRateTypeValidators(this.rateType());
  }

  private applyRateTypeValidators(type: 'fixed' | 'floating') {
    const rateValueCtrl = this.rateForm.get('rateValue');
    const additiveCtrl = this.rateForm.get('additivePercent');

    if (type === 'fixed') {
      rateValueCtrl?.setValidators([Validators.required, nonNegativeValidator(), Validators.max(9.9999)]);
      additiveCtrl?.setValidators([]);
    } else {
      additiveCtrl?.setValidators([Validators.required, nonNegativeValidator(), Validators.max(9.9999)]);
      rateValueCtrl?.setValidators([]);
    }

    rateValueCtrl?.updateValueAndValidity({ emitEvent: false });
    additiveCtrl?.updateValueAndValidity({ emitEvent: false });
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
    this.rateForm.patchValue({
      termFromMonths: p.minTermMonths,
      termToMonths: p.maxTermMonths,
    });
    this.refreshRateTermsValidationMessage();
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
    this.rateForm.patchValue({
      termFromMonths: 1,
      termToMonths: 12,
    });
    this.clearErrors();
  }

  clearErrors() {
    this.productError.set(null);
    this.currenciesError.set(null);
    this.interestRatesError.set(null);
    this.penaltiesError.set(null);
    this.error.set(null);
  }

  reloadDetails() {
    const id = this.selectedProductId();
    if (id === null) {
      return;
    }

    this.api.creditCurrencies(id).subscribe((r) => this.ccRows.set(r));
    this.api.interestRates(id).subscribe((r) => this.rateRows.set(r));
    this.api.penalties(id).subscribe((r) => this.penRows.set(r));
  }

  saveProduct() {
    if (this.productForm.invalid) {
      return;
    }

    const v = this.productForm.getRawValue() as CreditProductWriteDto;
    const id = this.selectedProductId();
    if (id === null) {
      this.api.createCreditProduct(v).subscribe({
        next: () => {
          this.reloadProducts();
          this.productError.set(null);
        },
        error: (e) => {
          const errorMessage = typeof e.error === 'string' ? e.error : e.error?.error;
          this.productError.set(errorMessage ?? 'Ошибка');
        },
      });
    } else {
      this.api.updateCreditProduct(id, v).subscribe({
        next: () => {
          this.reloadProducts();
          this.reloadDetails();
          this.productError.set(null);
        },
        error: (e) => {
          const errorMessage = typeof e.error === 'string' ? e.error : e.error?.error;
          this.productError.set(errorMessage ?? 'Ошибка');
        },
      });
    }
  }

  deleteProduct(p: CreditProductRow) {
    if (!confirm(`Удалить продукт «${p.name}»?`)) {
      return;
    }

    this.api.deleteCreditProduct(p.id).subscribe({
      next: () => {
        if (this.selectedProductId() === p.id) {
          this.clearProductSelection();
        }

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
    if (pid === null || this.ccForm.invalid) {
      return;
    }

    const v = this.ccForm.getRawValue();
    this.api.addCreditCurrency(pid, v).subscribe({
      next: () => {
        this.reloadDetails();
        this.currenciesError.set(null);
      },
      error: (e) => {
        const errorMessage = typeof e.error === 'string' ? e.error : e.error?.error;
        this.currenciesError.set(errorMessage ?? 'Ошибка');
      },
    });
  }

  removeCc(currencyCode: string) {
    if (!confirm('Удалить валюту из продукта?')) {
      return;
    }

    const pid = this.selectedProductId();
    const cid = this.currencyIdByCode(currencyCode);
    if (pid === null || cid === null) {
      return;
    }

    this.api.deleteCreditCurrency(pid, cid).subscribe({
      next: () => {
        this.error.set(null);
        this.reloadDetails();
      },
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  addRate() {
    const pid = this.selectedProductId();
    if (pid === null || !this.canAddRate()) {
      return;
    }

    const v = this.rateForm.getRawValue();

    // Validate term range
    if (v.termFromMonths > v.termToMonths) {
      this.interestRatesError.set(this.rateTermRangeError);
      return;
    }

    if (!this.isRateTermWithinProductBounds(v.termFromMonths, v.termToMonths)) {
      this.interestRatesError.set(this.rateTermBoundsError);
      return;
    }

    // Validate date range
    if (v.validTo && new Date(v.validFrom) > new Date(v.validTo)) {
      this.interestRatesError.set('Дата начала не может быть позже даты окончания');
      return;
    }

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
    const editingId = this.editingRateId();
    const request$ = editingId === null
      ? this.api.createInterestRate(body)
      : this.api.updateInterestRate(editingId, body);
    request$.subscribe({
      next: () => {
        const product = this.selectedProduct();
        this.rateForm.reset({
          currencyId: this.linkedCurrencyOptions()[0]?.id ?? 0,
          termFromMonths: product?.minTermMonths ?? 1,
          termToMonths: product?.maxTermMonths ?? 12,
          rateType: 'fixed',
          rateValue: null,
          additivePercent: null,
          validFrom: '',
          validTo: '',
        });
        this.reloadDetails();
        this.interestRatesError.set(null);
        this.rateType.set('fixed');
        this.applyRateTypeValidators('fixed');
        this.editingRateId.set(null);
      },
      error: (e) => {
        const errorMessage = typeof e.error === 'string' ? e.error : e.error?.error;
        this.interestRatesError.set(errorMessage ?? 'Ошибка');
      },
    });
  }

  editRate(r: InterestRateRow) {
    const currencyId = this.currencyIdByCode(r.currencyCode);
    if (currencyId === null) {
      this.interestRatesError.set('Валюта ставки не найдена.');
      return;
    }

    this.editingRateId.set(r.id);
    this.rateType.set(r.rateType === 'floating' ? 'floating' : 'fixed');
    this.rateForm.patchValue({
      currencyId,
      termFromMonths: r.termFromMonths,
      termToMonths: r.termToMonths,
      rateType: r.rateType,
      rateValue: r.rateType === 'fixed' ? r.rateValue : null,
      additivePercent: r.rateType === 'floating' ? r.additivePercent : null,
      validFrom: r.validFrom,
      validTo: r.validTo ?? '',
    });
    this.applyRateTypeValidators(this.rateType());
  }

  cancelRateEdit() {
    this.editingRateId.set(null);
    this.rateForm.patchValue({
      rateType: 'fixed',
      rateValue: null,
      additivePercent: null,
      validFrom: '',
      validTo: '',
    });
    this.rateType.set('fixed');
    this.applyRateTypeValidators('fixed');
  }

  deleteRate(r: InterestRateRow) {
    if (!confirm('Удалить процентную ставку?')) {
      return;
    }

    this.api.deleteInterestRate(r.id).subscribe({
      next: () => {
        this.error.set(null);
        this.reloadDetails();
      },
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  penaltyTypeLabel(type: string): string {
    if (type === 'early_repayment') {
      return 'Досрочное';
    }

    if (type === 'late_payment') {
      return 'Просрочка';
    }

    return type;
  }

  addPenalty() {
    const pid = this.selectedProductId();
    if (pid === null || this.penForm.invalid) {
      return;
    }

    const v = this.penForm.getRawValue();
    const body: PenaltyWriteDto = {
      creditId: pid,
      penaltyType: v.penaltyType,
      valuePercent: v.valuePercent,
      validFrom: v.validFrom,
    };
    const editingId = this.editingPenaltyId();
    const request$ = editingId === null
      ? this.api.createPenalty(body)
      : this.api.updatePenalty(editingId, body);
    request$.subscribe({
      next: () => {
        this.reloadDetails();
        this.penaltiesError.set(null);
        this.editingPenaltyId.set(null);
      },
      error: (e) => {
        const errorMessage = typeof e.error === 'string' ? e.error : e.error?.error;
        this.penaltiesError.set(errorMessage ?? 'Ошибка');
      },
    });
  }

  editPenalty(p: PenaltyRow) {
    this.editingPenaltyId.set(p.id);
    this.penForm.patchValue({
      penaltyType: p.penaltyType,
      valuePercent: p.valuePercent,
      validFrom: p.validFrom,
    });
  }

  cancelPenaltyEdit() {
    this.editingPenaltyId.set(null);
    this.penForm.patchValue({
      penaltyType: 'early_repayment',
      valuePercent: 0,
      validFrom: '',
    });
  }

  deletePenalty(p: PenaltyRow) {
    if (!confirm('Удалить штраф?')) {
      return;
    }

    this.api.deletePenalty(p.id).subscribe({
      next: () => {
        this.error.set(null);
        this.reloadDetails();
      },
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  rateTypeLabel(type: string): string {
    if (type === 'fixed') {
      return 'Фиксированная';
    }

    if (type === 'floating') {
      return 'Плавающая';
    }

    return type;
  }
}
