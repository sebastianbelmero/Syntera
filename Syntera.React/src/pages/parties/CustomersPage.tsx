import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Pencil } from "lucide-react";
import { customerApi } from "../../api/operations";
import type { CustomerDto, CustomerUpsertDto } from "../../types";
import { DataTable, type DataTableColumn } from "../../components/DataTable";
import { Modal, Field, inputClass, btnPrimary, btnGhost } from "../../components/Modal";
import { ApiError } from "../../api/client";

export default function CustomersPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<CustomerDto | null>(null);

  const columns: DataTableColumn<CustomerDto>[] = [
    {
      key: "name",
      header: "Pelanggan",
      render: (c) => (
        <div>
          <p className="font-medium">{c.name}</p>
          {c.contactPerson && (
            <p className="text-xs text-[var(--muted-foreground)]">{c.contactPerson}</p>
          )}
        </div>
      ),
    },
    {
      key: "contact",
      header: "Kontak",
      render: (c) => (
        <div className="text-xs">
          {c.email && <p>{c.email}</p>}
          {c.phone && <p className="text-[var(--muted-foreground)]">{c.phone}</p>}
        </div>
      ),
    },
    {
      key: "city",
      header: "Kota",
      render: (c) => <span className="text-xs">{c.city ?? "—"}</span>,
    },
    {
      key: "tax",
      header: "NPWP",
      render: (c) => (
        <span className="text-xs text-[var(--muted-foreground)]">{c.taxId ?? "—"}</span>
      ),
    },
    {
      key: "orders",
      header: "Total Order",
      render: (c) => <span className="text-xs">{c.totalOrders}</span>,
    },
    {
      key: "active",
      header: "Status",
      render: (c) => (
        <span
          className={`rounded-full px-2 py-1 text-xs ${
            c.isActive
              ? "bg-[var(--success)]/15 text-[var(--success)]"
              : "bg-[var(--muted)] text-[var(--muted-foreground)]"
          }`}
        >
          {c.isActive ? "Aktif" : "Nonaktif"}
        </span>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (c) => (
        <button
          type="button"
          onClick={() => setEditing(c)}
          className="ml-auto flex items-center gap-1 rounded-md p-1.5 text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--primary)]"
        >
          <Pencil size={16} />
        </button>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight">Pelanggan</h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Apotek, klinik, rumah sakit, dan pembeli B2B lainnya.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setCreateOpen(true)}
          className={`${btnPrimary} flex items-center gap-2`}
        >
          <Plus size={16} /> Pelanggan Baru
        </button>
      </header>

      <DataTable<CustomerDto>
        columns={columns}
        rowKey={(c) => c.id}
        load={async ({ page, pageSize, search }) => {
          const res = await customerApi.page({ page, pageSize, search });
          return { items: res.items, total: res.total, totalPages: res.totalPages };
        }}
      />

      {(createOpen || editing) && (
        <CustomerFormModal
          open
          customer={editing}
          onClose={() => {
            setCreateOpen(false);
            setEditing(null);
          }}
          onSaved={() => {
            void queryClient.invalidateQueries({ queryKey: ["customers"] });
            setCreateOpen(false);
            setEditing(null);
          }}
        />
      )}
    </div>
  );
}

function CustomerFormModal({
  open,
  customer,
  onClose,
  onSaved,
}: {
  open: boolean;
  customer: CustomerDto | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [submitting, setSubmitting] = useState(false);
  const isEdit = !!customer;

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      const fd = new FormData(e.currentTarget);
      const dto: CustomerUpsertDto = {
        name: String(fd.get("name") ?? ""),
        contactPerson: (fd.get("contactPerson") as string) || null,
        email: (fd.get("email") as string) || null,
        phone: (fd.get("phone") as string) || null,
        address: (fd.get("address") as string) || null,
        city: (fd.get("city") as string) || null,
        postalCode: (fd.get("postalCode") as string) || null,
        taxId: (fd.get("taxId") as string) || null,
        isActive: fd.get("isActive") === "on",
      };
      if (isEdit && customer) {
        await customerApi.update(customer.id, dto);
        toast.success("Pelanggan diperbarui.");
      } else {
        await customerApi.create(dto);
        toast.success("Pelanggan ditambahkan.");
      }
      onSaved();
    } catch (err) {
      toast.error((err as ApiError).message ?? "Gagal menyimpan.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={isEdit ? "Edit Pelanggan" : "Tambah Pelanggan"}
      size="xl"
      footer={
        <>
          <button type="button" className={btnGhost} onClick={onClose}>Batal</button>
          <button type="submit" form="customer-form" disabled={submitting} className={btnPrimary}>
            {submitting ? "Menyimpan…" : "Simpan"}
          </button>
        </>
      }
    >
      <form id="customer-form" onSubmit={handleSubmit} className="grid grid-cols-2 gap-3">
        <Field label="Nama Pelanggan" required>
          <input name="name" defaultValue={customer?.name} className={inputClass} required />
        </Field>
        <Field label="Narahubung">
          <input name="contactPerson" defaultValue={customer?.contactPerson ?? ""} className={inputClass} />
        </Field>
        <Field label="Email">
          <input name="email" type="email" defaultValue={customer?.email ?? ""} className={inputClass} />
        </Field>
        <Field label="Telepon">
          <input name="phone" defaultValue={customer?.phone ?? ""} className={inputClass} />
        </Field>
        <Field label="Alamat">
          <input name="address" defaultValue={customer?.address ?? ""} className={inputClass} />
        </Field>
        <Field label="Kota">
          <input name="city" defaultValue={customer?.city ?? ""} className={inputClass} />
        </Field>
        <Field label="Kode Pos">
          <input name="postalCode" defaultValue={customer?.postalCode ?? ""} className={inputClass} />
        </Field>
        <Field label="NPWP">
          <input name="taxId" defaultValue={customer?.taxId ?? ""} className={inputClass} />
        </Field>
        <label className="col-span-2 mt-2 flex items-center gap-2 text-sm">
          <input type="checkbox" name="isActive" defaultChecked={customer?.isActive ?? true} className="h-4 w-4" />
          Pelanggan aktif
        </label>
      </form>
    </Modal>
  );
}
