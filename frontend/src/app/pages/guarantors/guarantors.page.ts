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
  readonly draftPhysContracts = signal<ContractRow[]>([]);
  readonly phys = signal<PhysicalClientRow[]>([]);
  readonly selectedContractId = signal<number>(0);
  readonly error = signal<string | null>(null);

  readonly availablePhys = computed(() => {
    const contractId = this.selectedContractId();
    const ownerClientId = this.draftPhysContracts().find((c) => c.id === contractId)?.clientId;
    if (!ownerClientId) return this.phys();
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
    if (options.some((p) => p.clientId === current)) return;
    this.form.patchValue({ physPersonClientId: options[0]?.clientId ?? 0 });
  }

  add() {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.api
      .createGuarantor({ contractId: Number(v.contractId), physPersonClientId: Number(v.physPersonClientId) })
      .subscribe({
        next: () => {
          this.reload();
          this.error.set(null);
        },
        error: (e) =>
          this.error.set(typeof e.error === 'string' ? e.error : e.error?.title ?? 'Ошибка'),
      });
  }

  remove(g: GuarantorRow) {
    if (!confirm('Удалить поручителя?')) return;
    this.api.deleteGuarantor(g.internalId).subscribe({
      next: () => this.reload(),
      error: (e) => this.error.set(e.error ?? 'Ошибка'),
    });
  }
}
