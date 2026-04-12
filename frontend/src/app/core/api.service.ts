import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  currencies() {
    return this.http.get<CurrencyRow[]>(`${this.base}/currencies`);
  }
  createCurrency(body: CurrencyWriteDto) {
    return this.http.post<number>(`${this.base}/currencies`, body);
  }
  updateCurrency(id: number, body: CurrencyWriteDto) {
    return this.http.put(`${this.base}/currencies/${id}`, body);
  }
  deleteCurrency(id: number) {
    return this.http.delete(`${this.base}/currencies/${id}`);
  }

  refinanceRates() {
    return this.http.get<RefinanceRateRow[]>(`${this.base}/refinance-rates`);
  }
  createRefinanceRate(body: RefinanceRateWriteDto) {
    return this.http.post<number>(`${this.base}/refinance-rates`, body);
  }

  creditProducts() {
    return this.http.get<CreditProductRow[]>(`${this.base}/credit-products`);
  }
  createCreditProduct(body: CreditProductWriteDto) {
    return this.http.post<number>(`${this.base}/credit-products`, body);
  }
  updateCreditProduct(id: number, body: CreditProductWriteDto) {
    return this.http.put(`${this.base}/credit-products/${id}`, body);
  }
  deleteCreditProduct(id: number) {
    return this.http.delete(`${this.base}/credit-products/${id}`);
  }

  creditCurrencies(creditId: number) {
    return this.http.get<CreditCurrencyRow[]>(`${this.base}/credit-products/${creditId}/currencies`);
  }
  addCreditCurrency(creditId: number, body: CreditCurrencyWriteDto) {
    return this.http.post(`${this.base}/credit-products/${creditId}/currencies`, body);
  }
  deleteCreditCurrency(creditId: number, currencyId: number) {
    return this.http.delete(`${this.base}/credit-products/${creditId}/currencies/${currencyId}`);
  }

  interestRates(creditId: number) {
    return this.http.get<InterestRateRow[]>(`${this.base}/credit-products/${creditId}/interest-rates`);
  }
  createInterestRate(body: InterestRateWriteDto) {
    return this.http.post<number>(`${this.base}/credit-products/interest-rates`, body);
  }
  updateInterestRate(id: number, body: InterestRateWriteDto) {
    return this.http.put(`${this.base}/interest-rates/${id}`, body);
  }
  deleteInterestRate(id: number) {
    return this.http.delete(`${this.base}/interest-rates/${id}`);
  }

  penalties(creditId: number) {
    return this.http.get<PenaltyRow[]>(`${this.base}/credit-products/${creditId}/penalties`);
  }
  createPenalty(body: PenaltyWriteDto) {
    return this.http.post<number>(`${this.base}/credit-products/penalties`, body);
  }
  updatePenalty(id: number, body: PenaltyWriteDto) {
    return this.http.put(`${this.base}/penalties/${id}`, body);
  }
  deletePenalty(id: number) {
    return this.http.delete(`${this.base}/penalties/${id}`);
  }

  legalClients() {
    return this.http.get<LegalClientRow[]>(`${this.base}/clients/legal`);
  }
  createLegalClient(body: LegalClientDto) {
    return this.http.post<number>(`${this.base}/clients/legal`, body);
  }
  updateLegalClient(id: number, body: LegalClientDto) {
    return this.http.put(`${this.base}/clients/legal/${id}`, body);
  }

  physicalClients() {
    return this.http.get<PhysicalClientRow[]>(`${this.base}/clients/physical`);
  }
  createPhysicalClient(body: PhysicalClientDto) {
    return this.http.post<number>(`${this.base}/clients/physical`, body);
  }
  updatePhysicalClient(id: number, body: PhysicalClientDto) {
    return this.http.put(`${this.base}/clients/physical/${id}`, body);
  }
  deleteClient(id: number) {
    return this.http.delete(`${this.base}/clients/${id}`);
  }

  contracts() {
    return this.http.get<ContractRow[]>(`${this.base}/contracts`);
  }
  contractDetails(id: number) {
    return this.http.get<ContractDetailsDto>(`${this.base}/contracts/${id}`);
  }
  createContract(body: ContractCreateDto) {
    return this.http.post<number>(`${this.base}/contracts`, body);
  }
  updateContract(id: number, body: ContractUpdateDto) {
    return this.http.put(`${this.base}/contracts/${id}`, body);
  }
  deleteContract(id: number) {
    return this.http.delete(`${this.base}/contracts/${id}`);
  }
  signContract(id: number) {
    return this.http.post(`${this.base}/contracts/${id}/sign`, {});
  }

  guarantors() {
    return this.http.get<GuarantorRow[]>(`${this.base}/guarantors`);
  }
  createGuarantor(body: GuarantorCreateDto) {
    return this.http.post<number>(`${this.base}/guarantors`, body);
  }
  updateGuarantor(id: number, body: GuarantorCreateDto) {
    return this.http.put(`${this.base}/guarantors/${id}`, body);
  }
  deleteGuarantor(id: number) {
    return this.http.delete(`${this.base}/guarantors/${id}`);
  }

  pledges(contractId: number) {
    return this.http.get<PledgeRow[]>(`${this.base}/contracts/${contractId}/pledges`);
  }
  createPledge(contractId: number, body: PledgeWriteDto) {
    return this.http.post<number>(`${this.base}/contracts/${contractId}/pledges`, body);
  }
  updatePledge(id: number, body: PledgeWriteDto) {
    return this.http.put(`${this.base}/pledges/${id}`, body);
  }
  deletePledge(id: number) {
    return this.http.delete(`${this.base}/pledges/${id}`);
  }

  postPayment(contractId: number, body: PaymentCreateDto) {
    return this.http.post<number>(`${this.base}/contracts/${contractId}/payments`, body);
  }

  payments(contractId: number) {
    return this.http.get<PaymentRow[]>(`${this.base}/contracts/${contractId}/payments`);
  }

  paymentMinimum(contractId: number, paymentDate: string) {
    const params = new HttpParams().set('paymentDate', paymentDate);
    return this.http.get<PaymentMinimumDto>(`${this.base}/contracts/${contractId}/payments/minimum`, {
      params,
    });
  }

  reportExpectedPayments(q: Report7Query) {
    let p = new HttpParams()
      .set('creditId', String(q.creditId))
      .set('currencyId', String(q.currencyId))
      .set('contractAmount', String(q.contractAmount))
      .set('termMonths', String(q.termMonths))
      .set('issueDate', q.issueDate);
    return this.http.get<ExpectedPaymentsReportLineDto[]>(`${this.base}/reports/expected-payments`, {
      params: p,
    });
  }
  reportCurrentDebt(contractId: number) {
    return this.http.get<CurrentDebtReportDto>(`${this.base}/contracts/${contractId}/reports/current-debt`);
  }
  reportPaymentCalendar(contractId: number) {
    return this.http.get<PaymentCalendarLineDto[]>(
      `${this.base}/contracts/${contractId}/reports/payment-calendar`,
    );
  }
  reportCreditHistory(creditId: number) {
    return this.http.get<CreditHistoryEventDto[]>(`${this.base}/credit-products/${creditId}/reports/history`);
  }

  reportContractDistribution(q: ContractDistributionQuery) {
    let params = new HttpParams();

    if (q.groupBy) {
      params = params.set('groupBy', q.groupBy);
    }

    if (q.fromDate) {
      params = params.set('fromDate', q.fromDate);
    }

    if (q.toDate) {
      params = params.set('toDate', q.toDate);
    }

    return this.http.get<ContractDistributionReportRow[]>(`${this.base}/reports/contracts/distribution`, {
      params,
    });
  }

  reportClientCreditLoad() {
    return this.http.get<ClientCreditLoadReportRow[]>(`${this.base}/reports/client-credit-load`);
  }

  reportContractCollateral() {
    return this.http.get<ContractCollateralReportRow[]>(`${this.base}/reports/contracts/collateral`);
  }

  reportActiveClients() {
    return this.http.get<ActiveClientSummaryReportRow[]>(`${this.base}/reports/active-clients`);
  }

  reportCreditProductSummary() {
    return this.http.get<CreditProductSummaryReportRow[]>(`${this.base}/reports/credit-products/summary`);
  }

  reportNearCompletionContracts(thresholdPercent: number) {
    const params = new HttpParams().set('thresholdPercent', String(thresholdPercent));
    return this.http.get<NearCompletionContractReportRow[]>(
      `${this.base}/reports/contracts/nearing-completion`,
      { params },
    );
  }
}

export interface CurrencyRow {
  id: number;
  code: string;
  name: string;
}
export interface CurrencyWriteDto {
  code: string;
  name: string;
}
export interface RefinanceRateRow {
  id: number;
  validFromDate: string;
  validToDate: string | null;
  ratePercent: number;
}
export interface RefinanceRateWriteDto {
  validFromDate: string;
  validToDate: string | null;
  ratePercent: number;
}
export interface CreditProductRow {
  id: number;
  name: string;
  description: string | null;
  clientType: string;
  minAmount: number;
  maxAmount: number;
  minTermMonths: number;
  maxTermMonths: number;
}
export interface CreditProductWriteDto {
  name: string;
  description: string | null;
  clientType: string;
  minAmount: number;
  maxAmount: number;
  minTermMonths: number;
  maxTermMonths: number;
}
export interface CreditCurrencyRow {
  currencyCode: string;
}
export interface CreditCurrencyWriteDto {
  currencyId: number;
}
export interface InterestRateRow {
  id: number;
  currencyCode: string;
  termFromMonths: number;
  termToMonths: number;
  rateType: string;
  rateValue: number | null;
  additivePercent: number | null;
  validFrom: string;
  validTo: string | null;
}
export interface InterestRateWriteDto {
  creditId: number;
  currencyId: number;
  termFromMonths: number;
  termToMonths: number;
  rateType: string;
  rateValue: number | null;
  additivePercent: number | null;
  validFrom: string;
  validTo: string | null;
}
export interface PenaltyRow {
  id: number;
  penaltyType: string;
  valuePercent: number;
  validFrom: string;
}
export interface PenaltyWriteDto {
  creditId: number;
  penaltyType: string;
  valuePercent: number;
  validFrom: string;
}
export interface LegalClientRow {
  clientId: number;
  name: string;
  ownershipType: string;
  legalAddress: string;
  phone: string | null;
}
export interface LegalClientDto {
  name: string;
  ownershipType: string;
  legalAddress: string;
  phone: string | null;
}
export interface PhysicalClientRow {
  clientId: number;
  fullName: string;
  passportSeries: string;
  passportNumber: string;
  actualAddress: string | null;
  phone: string | null;
}
export interface PhysicalClientDto {
  fullName: string;
  passportSeries: string;
  passportNumber: string;
  actualAddress: string | null;
  phone: string | null;
}
export interface ContractRow {
  id: number;
  clientId: number;
  creditName: string;
  clientDisplay: string;
  clientType: string;
  currencyCode: string;
  contractAmount: number;
  termMonths: number;
  issueDate: string;
  status: string;
  remainingPrincipal: number;
  clientPassportSeries: string | null;
  clientPassportNumber: string | null;
}
export interface ContractDetailsDto {
  id: number;
  clientId: number;
  clientDisplay: string;
  clientType: string;
  clientPassportSeries: string | null;
  clientPassportNumber: string | null;
  creditId: number;
  creditName: string;
  currencyId: number;
  currencyCode: string;
  interestRateId: number | null;
  contractAmount: number;
  termMonths: number;
  issueDate: string;
  status: string;
  rateType: string;
  fixedInterestRate: number | null;
  fixedAdditivePercent: number | null;
  fixedEarlyPenaltyX: number | null;
  fixedLatePenaltyZ: number | null;
    guarantors: GuarantorRow[];
    pledges: PledgeRow[];
  remainingPrincipal: number;
}
export interface ContractCreateDto {
  clientId: number;
  creditId: number;
  currencyId: number;
  contractAmount: number;
  termMonths: number;
  issueDate: string;
}
export interface ContractUpdateDto {
  clientId?: number;
  creditId?: number;
  currencyId?: number;
  contractAmount?: number;
  termMonths?: number;
  issueDate?: string;
}
export interface GuarantorRow {
  internalId: number;
  contractId: number;
  physPersonClientId: number;
  contractCreditName: string;
  guarantorFullName: string;
  passportSeries: string;
  passportNumber: string;
}
export interface GuarantorCreateDto {
  contractId: number;
  physPersonClientId: number;
}
export interface PledgeRow {
  internalId: number;
  propertyName: string;
  estimatedValue: number;
  assessmentDate: string;
  propertyType: string;
  currencyCode: string | null;
}
export interface PledgeWriteDto {
    currencyId: number;
  propertyName: string;
  estimatedValue: number;
  assessmentDate: string;
  propertyType: string;
}
export interface PaymentCreateDto {
  paymentDate: string;
  totalAmount: number;
}
export interface PaymentRow {
  id: number;
  paymentDate: string;
  plannedPaymentDate: string;
  paymentType: string;
  principalAmount: number;
  interestAmount: number;
  earlyPenalty: number | null;
  latePenalty: number | null;
  totalAmount: number;
  remainingAfterPayment: number;
  appliedAnnualRate: number;
}
export interface PaymentMinimumDto {
  minimumAmount: number;
  interestAmount: number;
  latePenaltyAmount: number;
  maxAllowedAmount: number;
}
export interface ExpectedPaymentsReportLineDto {
  installmentNumber: number;
  plannedDate: string;
  expectedPayment: number;
}
export interface CurrentDebtReportDto {
  latePenaltyAccrued: number;
  interestDue: number;
  principalDueThisPeriod: number;
  remainingPrincipal: number;
}
export interface PaymentCalendarLineDto {
  plannedDate: string;
  expectedPayment: number;
  expectedPrincipal: number;
  expectedInterest: number;
  status: string;
}
export interface CreditHistoryEventDto {
  changeDate: string;
  kind: string | null;
  currencyCode: string | null;
  oldValuePercent: number | null;
  newValuePercent: number | null;
  oldTermFrom: number | null;
  oldTermTo: number | null;
  newTermFrom: number | null;
  newTermTo: number | null;
  penaltyType: string | null;
}
export interface Report7Query {
  creditId: number;
  currencyId: number;
  contractAmount: number;
  termMonths: number;
  issueDate: string;
}
export interface ContractDistributionQuery {
  groupBy: string;
  fromDate: string | null;
  toDate: string | null;
}
export interface ContractDistributionReportRow {
  groupValue: string;
  contractsCount: number;
  totalAmount: number;
  averageAmount: number;
}
export interface ClientCreditLoadReportRow {
  clientId: number;
  clientDisplay: string;
  clientType: string;
  activeContractsCount: number;
  completedContractsCount: number;
  totalIssuedAmount: number;
  totalRemainingPrincipal: number;
  averageTermMonths: number;
  averageInterestRate: number;
  overduePaymentsCount: number;
  scheduledPaymentsCount: number;
  overduePaymentShare: number;
}
export interface ContractCollateralReportRow {
  contractId: number;
  creditName: string;
  clientDisplay: string;
  currencyCode: string;
  contractAmount: number;
  remainingPrincipal: number;
  pledgeValue: number;
  coverageCoefficient: number;
  hasGuarantors: boolean;
  guarantorCount: number;
}
export interface ActiveClientSummaryReportRow {
  clientId: number;
  clientDisplay: string;
  activeContractsCount: number;
  totalIssuedAmount: number;
  totalRemainingPrincipal: number;
  averageMonthlyPayment: number;
}
export interface CreditProductSummaryReportRow {
  creditId: number;
  creditName: string;
  contractsCount: number;
  totalIssuedAmount: number;
  averageContractAmount: number;
  averageTermMonths: number;
}
export interface NearCompletionContractReportRow {
  contractId: number;
  creditName: string;
  clientDisplay: string;
  contractAmount: number;
  remainingPrincipal: number;
  repaidPercent: number;
  remainingPercent: number;
  expectedCompletionDate: string;
}
