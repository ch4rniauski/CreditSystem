import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, ContractRow } from '../../core/api.service';

@Component({
  selector: 'app-payments',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './payments.page.html',
  styleUrls: ['./payments.page.scss'],
})
export default class PaymentsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly signedContracts = signal<ContractRow[]>([]);
  readonly contractId = signal(0);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    paymentDate: ['', Validators.required],
    totalAmount: [0, [Validators.required, Validators.min(0.01)]],
  });

  ngOnInit() {
    this.api.contracts().subscribe((list) => {
      const signed = list.filter((c) => c.status === 'Оформлен' && c.remainingPrincipal > 0);
      this.signedContracts.set(signed);
      const first = signed[0]?.id ?? 0;
      this.contractId.set(first);
    });
  }

  setContract(id: number) {
    this.contractId.set(id);
  }

  submit() {
    const id = this.contractId();
    if (id === 0 || this.form.invalid) return;
    const v = this.form.getRawValue();
    this.api.postPayment(id, { paymentDate: v.paymentDate, totalAmount: v.totalAmount }).subscribe({
      next: () => {
        this.error.set(null);
        this.api.contracts().subscribe((list) => {
          const signed = list.filter((c) => c.status === 'Оформлен' && c.remainingPrincipal > 0);
          this.signedContracts.set(signed);
        });
      },
      error: (e) =>
        this.error.set(typeof e.error === 'string' ? e.error : e.error?.title ?? 'Ошибка'),
    });
  }
}
