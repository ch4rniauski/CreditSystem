import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, ContractRow, GuarantorRow, PhysicalClientRow } from '../../core/api.service';

@Component({
  selector: 'app-guarantors',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './guarantors.page.html',
  styleUrls: ['./guarantors.page.scss'],
})
export default class GuarantorsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly guarantors = signal<GuarantorRow[]>([]);
  readonly search = signal('');
  readonly contractFilter = signal('all');
  readonly sortBy = signal('nameAsc');
  readonly draftPhysContracts = signal<ContractRow[]>([]);
  readonly phys = signal<PhysicalClientRow[]>([]);
  readonly selectedContractId = signal<number>(0);
  readonly editingGuarantorId = signal<number | null>(null);
  readonly error = signal<string | null>(null);

  readonly filteredGuarantors = computed(() => {
    const term = this.search().trim().toLowerCase();
    const contractFilter = this.contractFilter();
    const sortBy = this.sortBy();

    const filtered = this.guarantors().filter((row) => {
      const matchesSearch =
        term.length === 0 ||
        row.contractCreditName.toLowerCase().includes(term) ||
        row.guarantorFullName.toLowerCase().includes(term) ||
        row.passportSeries.toLowerCase().includes(term) ||
        row.passportNumber.toLowerCase().includes(term);

      const matchesContract = contractFilter === 'all' || String(row.contractId) === contractFilter;
      return matchesSearch && matchesContract;
    });

    return [...filtered].sort((a, b) => {
      if (sortBy === 'nameDesc') {
        return b.guarantorFullName.localeCompare(a.guarantorFullName);
      }

      if (sortBy === 'contractAsc') {
        return a.contractId - b.contractId;
      }

      if (sortBy === 'contractDesc') {
        return b.contractId - a.contractId;
      }

      return a.guarantorFullName.localeCompare(b.guarantorFullName);
    });
  });

  readonly availablePhys = computed(() => {
    const contractId = this.selectedContractId();
    const ownerClientId = this.draftPhysContracts().find((c) => c.id === contractId)?.clientId;
    if (!ownerClientId) {
      return this.phys();
    }

    return this.phys().filter((p) => p.clientId !== ownerClientId);
  });

  readonly form = this.fb.nonNullable.group({
    contractId: [0, Validators.min(1)],
    physPersonClientId: [0, Validators.min(1)],
  });

  ngOnInit() {
    this.reload();
    this.api.physicalClients().subscribe((p) => {
      this.phys.set(p);
      this.ensureGuarantorSelection();
    });

    this.form.get('contractId')?.valueChanges.subscribe((value) => {
      this.selectedContractId.set(Number(value) || 0);
      this.ensureGuarantorSelection();
    });
  }

  reload() {
    this.api.guarantors().subscribe((g) => this.guarantors.set(g));
    this.api.contracts().subscribe((list) => {
      const drafts = list.filter((c) => c.status === 'Оформляется' && c.clientType === 'physical');
      this.draftPhysContracts.set(drafts);
      const first = drafts[0]?.id ?? 0;
      this.selectedContractId.set(first);
      this.form.patchValue({ contractId: first });
      this.ensureGuarantorSelection();
    });
  }

  private ensureGuarantorSelection() {
    const options = this.availablePhys();
    const current = Number(this.form.getRawValue().physPersonClientId);
    if (options.some((p) => p.clientId === current)) {
      return;
    }

    this.form.patchValue({ physPersonClientId: options[0]?.clientId ?? 0 });
  }

  add() {
    if (this.form.invalid) {
      return;
    }

    const v = this.form.getRawValue();
    const payload = { contractId: Number(v.contractId), physPersonClientId: Number(v.physPersonClientId) };
    const editingId = this.editingGuarantorId();
    const request$ = editingId === null
      ? this.api.createGuarantor(payload)
      : this.api.updateGuarantor(editingId, payload);

    request$.subscribe({
        next: () => {
          this.reload();
          this.editingGuarantorId.set(null);
          this.error.set(null);
        },
        error: (e) =>
          this.error.set(typeof e.error === 'string' ? e.error : e.error?.title ?? 'Ошибка'),
      });
  }

  edit(g: GuarantorRow) {
    this.editingGuarantorId.set(g.internalId);
    this.selectedContractId.set(g.contractId);
    this.form.patchValue({
      contractId: g.contractId,
      physPersonClientId: g.physPersonClientId,
    });
    this.ensureGuarantorSelection();
  }

  cancelEdit() {
    this.editingGuarantorId.set(null);
    this.ensureGuarantorSelection();
  }

  remove(g: GuarantorRow) {
    if (!confirm('Удалить поручителя?')) {
      return;
    }

    this.api.deleteGuarantor(g.internalId).subscribe({
      next: () => {
        this.error.set(null);
        this.reload();
      },
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }

  setSearch(value: string) {
    this.search.set(value);
  }

  setContractFilter(value: string) {
    this.contractFilter.set(value);
  }

  setSortBy(value: string) {
    this.sortBy.set(value);
  }
}
