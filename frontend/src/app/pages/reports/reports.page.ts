import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  ApiService,
  ActiveClientSummaryReportRow,
  ClientCreditLoadReportRow,
  ContractCollateralReportRow,
  ContractDistributionQuery,
  ContractDistributionReportRow,
  ContractRow,
  CreditHistoryEventDto,
  CreditProductSummaryReportRow,
  CreditProductRow,
  CurrentDebtReportDto,
  ExpectedPaymentsReportLineDto,
  NearCompletionContractReportRow,
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
  readonly contractDistribution = signal<ContractDistributionReportRow[]>([]);
  readonly contractDistributionVisible = signal(false);
  readonly contractDistributionError = signal<string | null>(null);
  readonly contractDistributionTotal = computed(() =>
    this.contractDistribution().reduce((sum, row) => sum + row.contractsCount, 0),
  );
  readonly clientLoad = signal<ClientCreditLoadReportRow[]>([]);
  readonly clientLoadVisible = signal(false);
  readonly clientLoadError = signal<string | null>(null);
  readonly collateral = signal<ContractCollateralReportRow[]>([]);
  readonly collateralVisible = signal(false);
  readonly collateralError = signal<string | null>(null);
  readonly activeClients = signal<ActiveClientSummaryReportRow[]>([]);
  readonly activeClientsVisible = signal(false);
  readonly activeClientsError = signal<string | null>(null);
  readonly productSummary = signal<CreditProductSummaryReportRow[]>([]);
  readonly productSummaryVisible = signal(false);
  readonly productSummaryError = signal<string | null>(null);
  readonly nearCompletion = signal<NearCompletionContractReportRow[]>([]);
  readonly nearCompletionVisible = signal(false);
  readonly nearCompletionError = signal<string | null>(null);
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

  readonly distributionForm = this.fb.nonNullable.group({
    groupBy: ['status'],
    fromDate: [''],
    toDate: [''],
  });

  readonly nearCompletionForm = this.fb.nonNullable.group({
    thresholdPercent: [20, [Validators.required, Validators.min(0.01), Validators.max(100)]],
  });

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

  runDistribution() {
    this.contractDistributionError.set(null);
    const value = this.distributionForm.getRawValue();

    const query: ContractDistributionQuery = {
      groupBy: value.groupBy,
      fromDate: value.fromDate || null,
      toDate: value.toDate || null,
    };

    this.api.reportContractDistribution(query).subscribe({
      next: (rows) => {
        this.contractDistribution.set(rows);
        this.contractDistributionVisible.set(true);
      },
      error: (e) => {
        this.contractDistribution.set([]);
        this.contractDistributionVisible.set(false);
        this.contractDistributionError.set(this.apiErrorMessage(e, 'Не удалось загрузить отчет по договорам.'));
      },
    });
  }

  hideDistribution() {
    this.contractDistributionVisible.set(false);
  }

  runClientLoad() {
    this.clientLoadError.set(null);
    this.api.reportClientCreditLoad().subscribe({
      next: (rows) => {
        this.clientLoad.set(rows);
        this.clientLoadVisible.set(true);
      },
      error: (e) => {
        this.clientLoad.set([]);
        this.clientLoadVisible.set(false);
        this.clientLoadError.set(this.apiErrorMessage(e, 'Не удалось загрузить портрет кредитной нагрузки.'));
      },
    });
  }

  hideClientLoad() {
    this.clientLoadVisible.set(false);
  }

  runCollateral() {
    this.collateralError.set(null);
    this.api.reportContractCollateral().subscribe({
      next: (rows) => {
        this.collateral.set(rows);
        this.collateralVisible.set(true);
      },
      error: (e) => {
        this.collateral.set([]);
        this.collateralVisible.set(false);
        this.collateralError.set(this.apiErrorMessage(e, 'Не удалось загрузить отчет по обеспечению.'));
      },
    });
  }

  hideCollateral() {
    this.collateralVisible.set(false);
  }

  runActiveClients() {
    this.activeClientsError.set(null);
    this.api.reportActiveClients().subscribe({
      next: (rows) => {
        this.activeClients.set(rows);
        this.activeClientsVisible.set(true);
      },
      error: (e) => {
        this.activeClients.set([]);
        this.activeClientsVisible.set(false);
        this.activeClientsError.set(this.apiErrorMessage(e, 'Не удалось загрузить отчет по активным клиентам.'));
      },
    });
  }

  hideActiveClients() {
    this.activeClientsVisible.set(false);
  }

  runProductSummary() {
    this.productSummaryError.set(null);
    this.api.reportCreditProductSummary().subscribe({
      next: (rows) => {
        this.productSummary.set(rows);
        this.productSummaryVisible.set(true);
      },
      error: (e) => {
        this.productSummary.set([]);
        this.productSummaryVisible.set(false);
        this.productSummaryError.set(this.apiErrorMessage(e, 'Не удалось загрузить сводку по кредитным продуктам.'));
      },
    });
  }

  hideProductSummary() {
    this.productSummaryVisible.set(false);
  }

  runNearCompletion() {
    if (this.nearCompletionForm.invalid) {
      return;
    }

    this.nearCompletionError.set(null);
    const thresholdPercent = this.nearCompletionForm.getRawValue().thresholdPercent;

    this.api.reportNearCompletionContracts(thresholdPercent).subscribe({
      next: (rows) => {
        this.nearCompletion.set(rows);
        this.nearCompletionVisible.set(true);
      },
      error: (e) => {
        this.nearCompletion.set([]);
        this.nearCompletionVisible.set(false);
        this.nearCompletionError.set(this.apiErrorMessage(e, 'Не удалось загрузить отчет по договорам с малым остатком долга.'));
      },
    });
  }

  hideNearCompletion() {
    this.nearCompletionVisible.set(false);
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

  private apiErrorMessage(error: unknown, fallback: string): string {
    if (typeof error === 'object' && error && 'error' in error) {
      const maybeError = (error as { error?: unknown }).error;
      if (typeof maybeError === 'string') {
        return maybeError;
      }

      if (maybeError && typeof maybeError === 'object' && 'error' in maybeError) {
        const nested = (maybeError as { error?: unknown }).error;
        if (typeof nested === 'string') {
          return nested;
        }
      }
    }

    return fallback;
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
