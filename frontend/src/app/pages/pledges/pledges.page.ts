import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, ContractRow, CurrencyRow, PledgeRow } from '../../core/api.service';

@Component({
  selector: 'app-pledges',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pledges.page.html',
  styleUrls: ['./pledges.page.scss'],
})
export default class PledgesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly draftContracts = signal<ContractRow[]>([]);
  readonly currencies = signal<CurrencyRow[]>([]);
  readonly selectedContractId = signal(0);
  readonly pledges = signal<PledgeRow[]>([]);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    propertyName: ['', Validators.required],
    estimatedValue: [1, [Validators.required, Validators.min(0.01)]],
    assessmentDate: ['', Validators.required],
    propertyType: ['real_estate', Validators.required],
    currencyId: [0, Validators.min(1)],
  });

  ngOnInit() {
    this.api.currencies().subscribe((c) => {
      this.currencies.set(c);
      const first = c[0]?.id ?? 0;
      if (first) {
        this.form.patchValue({ currencyId: first });
      }
    });

    this.api.contracts().subscribe((list) => {
      const drafts = list.filter((c) => c.status === 'Оформляется');
      this.draftContracts.set(drafts);
      const first = drafts[0]?.id ?? 0;
      this.selectedContractId.set(first);
      if (first) {
        this.loadPledges(first);
      }
    });
  }

  pickContract(id: number) {
    this.selectedContractId.set(id);
    if (id) {
      this.loadPledges(id);
    }
  }

  loadPledges(contractId: number) {
    this.api.pledges(contractId).subscribe((p) => this.pledges.set(p));
  }

  add() {
    const cid = this.selectedContractId();
    if (cid === 0 || this.form.invalid) {
      return;
    }

    this.api.createPledge(cid, this.form.getRawValue()).subscribe({
      next: () => {
        this.form.reset({
          propertyName: '',
          estimatedValue: 1,
          assessmentDate: '',
          propertyType: 'real_estate',
          currencyId: this.currencies()[0]?.id ?? 0,
        });
        this.loadPledges(cid);
      },
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  remove(p: PledgeRow) {
    if (!confirm('Удалить залог?')) {
      return;
    }

    this.api.deletePledge(p.internalId).subscribe({
      next: () => this.loadPledges(this.selectedContractId()),

      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  pledgeTypeLabel(value: string): string {
    const labels: { [key: string]: string } = {
      real_estate: 'Недвижимость',
      vehicle: 'Автотранспорт',
      equipment: 'Оборудование',
    };

    return labels[value] ?? value;
  }

  contractClientLabel(c: ContractRow): string {
    if (c.clientPassportSeries && c.clientPassportNumber) {
      return `${c.clientDisplay} (${c.clientPassportSeries} ${c.clientPassportNumber})`;
    }

    return c.clientDisplay;
  }
}
