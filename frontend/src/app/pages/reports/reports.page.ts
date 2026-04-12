import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  ApiService,
  ContractRow,
  CreditHistoryEventDto,
  CreditProductRow,
  CurrentDebtReportDto,
  ExpectedPaymentsReportLineDto,
  PaymentCalendarLineDto,
} from '../../core/api.service';

@Component({
  selector: 'app-reports',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reports.page.html',
  styleUrls: ['./reports.page.scss'],
})
export default class ReportsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly credits = signal<CreditProductRow[]>([]);
  readonly currencies = signal<{ id: number; code: string }[]>([]);
  readonly expected = signal<ExpectedPaymentsReportLineDto[]>([]);
  readonly expectedVisible = signal(false);
  readonly expectedError = signal<string | null>(null);
  readonly reportContracts = signal<ContractRow[]>([]);
  readonly contractPick = signal(0);
  readonly debt = signal<CurrentDebtReportDto | null>(null);
  readonly debtVisible = signal(false);
  readonly calendar = signal<PaymentCalendarLineDto[]>([]);
  readonly calendarVisible = signal(false);
  readonly historyCreditId = signal(0);
  readonly history = signal<CreditHistoryEventDto[]>([]);
  readonly historyVisible = signal(false);
  readonly historyLoaded = signal(false);

  readonly r7Form = this.fb.nonNullable.group({
    creditId: [0, Validators.min(1)],
    currencyId: [0, Validators.min(1)],
    contractAmount: [10000, [Validators.required, Validators.min(0.01)]],
    termMonths: [12, [Validators.required, Validators.min(1)]],
    issueDate: ['', Validators.required],
  });

  ngOnInit() {
    this.api.creditProducts().subscribe((p) => {
      this.credits.set(p);
      const first = p[0]?.id ?? 0;
      if (first) {
        this.r7Form.patchValue({ creditId: first });
        this.historyCreditId.set(first);
      }
    });
    this.api.currencies().subscribe((c) => {
      this.currencies.set(c.map((x) => ({ id: x.id, code: x.code })));
      const first = c[0]?.id ?? 0;
      if (first) {
        this.r7Form.patchValue({ currencyId: first });
      }
    });
    this.api.contracts().subscribe((list) => {
      const ok = list.filter((x) => x.status !== 'Оформляется');
      this.reportContracts.set(ok);
      const first = ok[0]?.id ?? 0;
      this.contractPick.set(first);
    });
  }

  onHistoryCreditChange(id: number) {
    this.historyCreditId.set(id);
    this.loadHistory();
  }

  runExpected() {
    if (this.r7Form.invalid) {
      return;
    }

    this.expectedError.set(null);
    const v = this.r7Form.getRawValue();
    this.api
      .reportExpectedPayments({
        creditId: Number(v.creditId),
        currencyId: Number(v.currencyId),
        contractAmount: v.contractAmount,
        termMonths: v.termMonths,
        issueDate: v.issueDate,
      })
      .subscribe({
        next: (r) => {
          this.expected.set(r);
          this.expectedVisible.set(true);
        },
        error: (e) => {
          this.expected.set([]);
          this.expectedVisible.set(false);
          this.expectedError.set(typeof e.error === 'string' ? e.error : e.error?.error ?? 'Не удалось рассчитать ожидаемые платежи.');
        },
      });
  }

  hideExpected() {
    this.expectedVisible.set(false);
    this.expectedError.set(null);
  }

  pickContract(id: number) {
    this.contractPick.set(id);
  }

  loadDebt() {
    const id = this.contractPick();
    if (!id) {
      return;
    }

    this.api.reportCurrentDebt(id).subscribe({
      next: (d) => {
        this.debt.set(d);
        this.debtVisible.set(true);
      },
      error: () => this.debt.set(null),
    });
  }

  hideDebt() {
    this.debtVisible.set(false);
  }

  loadCalendar() {
    const id = this.contractPick();
    if (!id) {
      return;
    }

    this.api.reportPaymentCalendar(id).subscribe((c) => {
      this.calendar.set(c);
      this.calendarVisible.set(true);
    });
  }

  hideCalendar() {
    this.calendarVisible.set(false);
  }

  loadHistory() {
    const id = this.historyCreditId();
    if (!id) {
      return;
    }

    this.historyVisible.set(true);
    this.historyLoaded.set(false);

    this.api.reportCreditHistory(id).subscribe({
      next: (h) => {
        this.history.set(h);
        this.historyVisible.set(true);
        this.historyLoaded.set(true);
      },
      error: () => {
        this.history.set([]);
        this.historyVisible.set(true);
        this.historyLoaded.set(true);
      },
    });
  }

  hideHistory() {
    this.historyVisible.set(false);
  }

  historyKindLabel(kind: string | null): string {
    if (kind === 'interest_rate') {
      return 'Процентная ставка';
    }

    if (kind === 'penalty') {
      return 'Штраф';
    }

    return kind ?? '';
  }

  historyPenaltyLabel(penaltyType: string | null): string {
    if (penaltyType === 'early_repayment') {
      return 'досрочное погашение';
    }

    if (penaltyType === 'late_payment') {
      return 'просрочка';
    }

    return '';
  }

  historyTypeLabel(kind: string | null, penaltyType: string | null): string {
    const baseType = this.historyKindLabel(kind);
    const penaltyLabel = this.historyPenaltyLabel(penaltyType);

    if (baseType && penaltyLabel) {
      return `${baseType} (${penaltyLabel})`;
    }

    return baseType || penaltyLabel;
  }
}
