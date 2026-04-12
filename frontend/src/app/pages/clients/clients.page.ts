import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, LegalClientRow, PhysicalClientRow } from '../../core/api.service';
import { phoneValidator, passportSeriesValidator, passportNumberValidator } from '../../core/validators';

@Component({
  selector: 'app-clients',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './clients.page.html',
  styleUrls: ['./clients.page.scss'],
})
export default class ClientsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly legalRows = signal<LegalClientRow[]>([]);
  readonly physRows = signal<PhysicalClientRow[]>([]);
  readonly legalSearch = signal('');
  readonly legalSortBy = signal('nameAsc');
  readonly physSearch = signal('');
  readonly physSortBy = signal('nameAsc');
  readonly legalEditId = signal<number | null>(null);
  readonly physEditId = signal<number | null>(null);
  readonly error = signal<string | null>(null);

  readonly filteredLegalRows = computed(() => {
    const term = this.legalSearch().trim().toLowerCase();
    const sortBy = this.legalSortBy();

    const filtered = this.legalRows().filter((row) =>
      term.length === 0 ||
      row.name.toLowerCase().includes(term) ||
      row.ownershipType.toLowerCase().includes(term) ||
      row.legalAddress.toLowerCase().includes(term) ||
      (row.phone ?? '').toLowerCase().includes(term));

    return [...filtered].sort((a, b) => {
      if (sortBy === 'nameDesc') {
        return b.name.localeCompare(a.name);
      }

      return a.name.localeCompare(b.name);
    });
  });

  readonly filteredPhysRows = computed(() => {
    const term = this.physSearch().trim().toLowerCase();
    const normalizedTerm = term.replace(/\s+/g, '').toUpperCase();
    const sortBy = this.physSortBy();

    const filtered = this.physRows().filter((row) => {
      const combinedPassport = `${row.passportSeries}${row.passportNumber}`.toUpperCase();
      const spacedPassport = `${row.passportSeries} ${row.passportNumber}`.toLowerCase();

      return term.length === 0 ||
        row.fullName.toLowerCase().includes(term) ||
        row.passportSeries.toLowerCase().includes(term) ||
        row.passportNumber.toLowerCase().includes(term) ||
        combinedPassport.includes(normalizedTerm) ||
        spacedPassport.includes(term) ||
        (row.actualAddress ?? '').toLowerCase().includes(term) ||
        (row.phone ?? '').toLowerCase().includes(term);
    });

    return [...filtered].sort((a, b) => {
      if (sortBy === 'nameDesc') {
        return b.fullName.localeCompare(a.fullName);
      }

      return a.fullName.localeCompare(b.fullName);
    });
  });

  readonly legalForm = this.fb.nonNullable.group({
    name: ['', Validators.required],
    ownershipType: ['', Validators.required],
    legalAddress: ['', Validators.required],
    phone: ['', [phoneValidator()]],
  });

  readonly physForm = this.fb.nonNullable.group({
    fullName: ['', Validators.required],
    passportSeries: ['', [Validators.required, passportSeriesValidator()]],
    passportNumber: ['', [Validators.required, passportNumberValidator()]],
    actualAddress: [''],
    phone: ['', [phoneValidator()]],
  });

  ngOnInit() {
    this.reload();
  }

  reload() {
    this.api.legalClients().subscribe((r) => this.legalRows.set(r));
    this.api.physicalClients().subscribe((r) => this.physRows.set(r));
  }

  editLegal(r: LegalClientRow) {
    this.legalEditId.set(r.clientId);
    this.legalForm.patchValue({
      name: r.name,
      ownershipType: r.ownershipType,
      legalAddress: r.legalAddress,
      phone: r.phone ?? '',
    });
  }

  cancelLegal() {
    this.legalEditId.set(null);
    this.legalForm.reset();
  }

  saveLegal() {
    if (this.legalForm.invalid) {
      return;
    }

    const v = this.legalForm.getRawValue();
    const id = this.legalEditId();
    if (id === null) {
      this.api.createLegalClient(v).subscribe({
        next: () => {
          this.cancelLegal();
          this.error.set(null);
          this.reload();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    } else {
      this.api.updateLegalClient(id, v).subscribe({
        next: () => {
          this.cancelLegal();
          this.error.set(null);
          this.reload();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    }
  }

  editPhys(r: PhysicalClientRow) {
    this.physEditId.set(r.clientId);
    this.physForm.patchValue({
      fullName: r.fullName,
      passportSeries: r.passportSeries,
      passportNumber: r.passportNumber,
      actualAddress: r.actualAddress ?? '',
      phone: r.phone ?? '',
    });
  }

  cancelPhys() {
    this.physEditId.set(null);
    this.physForm.reset();
  }

  savePhys() {
    if (this.physForm.invalid) {
      return;
    }

    const v = this.physForm.getRawValue();
    const id = this.physEditId();
    if (id === null) {
      this.api.createPhysicalClient(v).subscribe({
        next: () => {
          this.cancelPhys();
          this.error.set(null);
          this.reload();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    } else {
      this.api.updatePhysicalClient(id, v).subscribe({
        next: () => {
          this.cancelPhys();
          this.error.set(null);
          this.reload();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    }
  }

  deleteClient(clientId: number, label: string) {
    if (!confirm(`Удалить «${label}»?`)) {
      return;
    }

    this.api.deleteClient(clientId).subscribe({
      next: () => {
        this.error.set(null);
        this.reload();
      },
      error: (e) => this.error.set(typeof e.error === 'string' ? e.error : 'Невозможно удалить'),
    });
  }

  setLegalSearch(value: string) {
    this.legalSearch.set(value);
  }

  setLegalSortBy(value: string) {
    this.legalSortBy.set(value);
  }

  setPhysSearch(value: string) {
    this.physSearch.set(value);
  }

  setPhysSortBy(value: string) {
    this.physSortBy.set(value);
  }
}
