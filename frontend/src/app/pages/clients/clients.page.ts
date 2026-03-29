import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
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
  readonly legalEditId = signal<number | null>(null);
  readonly physEditId = signal<number | null>(null);
  readonly error = signal<string | null>(null);

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
    if (this.legalForm.invalid) return;
    const v = this.legalForm.getRawValue();
    const id = this.legalEditId();
    if (id === null) {
      this.api.createLegalClient(v).subscribe({
        next: () => {
          this.cancelLegal();
          this.reload();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    } else {
      this.api.updateLegalClient(id, v).subscribe({
        next: () => {
          this.cancelLegal();
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
    if (this.physForm.invalid) return;
    const v = this.physForm.getRawValue();
    const id = this.physEditId();
    if (id === null) {
      this.api.createPhysicalClient(v).subscribe({
        next: () => {
          this.cancelPhys();
          this.reload();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    } else {
      this.api.updatePhysicalClient(id, v).subscribe({
        next: () => {
          this.cancelPhys();
          this.reload();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    }
  }

  deleteClient(clientId: number, label: string) {
    if (!confirm(`Удалить «${label}»?`)) return;
    this.api.deleteClient(clientId).subscribe({
      next: () => this.reload(),
      error: (e) => this.error.set(typeof e.error === 'string' ? e.error : 'Невозможно удалить'),
    });
  }
}
