import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Pencil } from "lucide-react";
import { supplierApi } from "../../api/catalog";
import type { SupplierDto, SupplierUpsertDto } from "../../types";
import { formatDate } from "../../lib/format";
import { AppGrid, type AppGridColumn } from "../../components/AppGrid";
import { Modal, Field, inputClass, btnPrimary, btnGhost } from "../../components/Modal";
import { ApiError } from "../../api/client";

export default function SuppliersPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<SupplierDto | null>(null);

  const columns: AppGridColumn<SupplierDto>[] = [
    {
      key: "name",
      header: "Pemasok",
      sortable: true,
      sortAccessor: (s) => s.name,
      render: (s) => (
        <div>
          <p className="font-medium">{s.name}</p>
          {s.contactPerson && (
            <p className="text-xs text-[var(--muted-foreground)]">{s.contactPerson}</p>
          )}
        </div>
      ),
    },
    {
      key: "contact",
      header: "Kontak",
      render: (s) => (
        <div className="text-xs">
          {s.email && <p>{s.email}</p>}
          {s.phone && <p className="text-[var(--muted-foreground)]">{s.phone}</p>}
        </div>
      ),
    },
    {
      key: "city",
      header: "Kota",
      hideOnMobile: true,
      sortable: true,
      sortAccessor: (s) => s.city ?? "",
      render: (s) => <span className="text-xs">{s.city ?? "—"}</span>,
    },
    {
      key: "license",
      header: "Lisensi",
      hideOnMobile: true,
      render: (s) => (
        <span className="text-xs text-[var(--muted-foreground)]">
          {s.licenseNumber ?? "—"}
        </span>
      ),
    },
    {
      key: "active",
      header: "Status",
      sortable: true,
      sortAccessor: (s) => (s.isActive ? 1 : 0),
      render: (s) => (
        <span
          className={`rounded-full px-2 py-1 text-xs ${
            s.isActive
              ? "bg-[var(--success)]/15 text-[var(--success)]"
              : "bg-[var(--muted)] text-[var(--muted-foreground)]"
          }`}
        >
          {s.isActive ? "Aktif" : "Nonaktif"}
        </span>
      ),
    },
    {
      key: "created",
      header: "Bergabung",
      hideOnMobile: true,
      sortable: true,
      sortAccessor: (s) => s.createdAt,
      render: (s) => <span className="text-xs">{formatDate(s.createdAt)}</span>,
    },
    {
      key: "actions",
      header: "",
      align: "right",
      render: (s) => (
        <button
          type="button"
          onClick={() => setEditing(s)}
          aria-label={`Edit pemasok ${s.name}`}
          title="Edit pemasok"
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
          <h2 className="text-2xl font-bold tracking-tight">Pemasok</h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Distributor dan produsen obat.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setCreateOpen(true)}
          className={`${btnPrimary} flex items-center gap-2`}
        >
          <Plus size={16} /> Pemasok Baru
        </button>
      </header>

      <AppGrid<SupplierDto>
        columns={columns}
        rowKey={(s) => s.id}
        load={async ({ page, pageSize, search }) => {
          const res = await supplierApi.page({ page, pageSize, search });
          return { items: res.items, total: res.total, totalPages: res.totalPages };
        }}
      />

      {(createOpen || editing) && (
        <SupplierFormModal
          open
          supplier={editing}
          onClose={() => {
            setCreateOpen(false);
            setEditing(null);
          }}
          onSaved={() => {
            void queryClient.invalidateQueries({ queryKey: ["suppliers"] });
            void queryClient.invalidateQueries({ queryKey: ["suppliers-list"] });
            setCreateOpen(false);
            setEditing(null);
          }}
        />
      )}
    </div>
  );
}

function SupplierFormModal({
  open,
  supplier,
  onClose,
  onSaved,
}: {
  open: boolean;
  supplier: SupplierDto | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [submitting, setSubmitting] = useState(false);
  const isEdit = !!supplier;

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      const fd = new FormData(e.currentTarget);
      const dto: SupplierUpsertDto = {
        name: String(fd.get("name") ?? ""),
        contactPerson: (fd.get("contactPerson") as string) || null,
        email: (fd.get("email") as string) || null,
        phone: (fd.get("phone") as string) || null,
        address: (fd.get("address") as string) || null,
        city: (fd.get("city") as string) || null,
        postalCode: (fd.get("postalCode") as string) || null,
        licenseNumber: (fd.get("licenseNumber") as string) || null,
        isActive: fd.get("isActive") === "on",
      };
      if (isEdit && supplier) {
        await supplierApi.update(supplier.id, dto);
        toast.success("Pemasok diperbarui.");
      } else {
        await supplierApi.create(dto);
        toast.success("Pemasok ditambahkan.");
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
      title={isEdit ? "Edit Pemasok" : "Tambah Pemasok"}
      size="xl"
      footer={
        <>
          <button type="button" className={btnGhost} onClick={onClose}>Batal</button>
          <button type="submit" form="supplier-form" disabled={submitting} className={btnPrimary}>
            {submitting ? "Menyimpan…" : "Simpan"}
          </button>
        </>
      }
    >
      <form id="supplier-form" onSubmit={handleSubmit} className="grid grid-cols-2 gap-3">
        <Field label="Nama Pemasok" required>
          <input name="name" defaultValue={supplier?.name} className={inputClass} required />
        </Field>
        <Field label="Narahubung">
          <input name="contactPerson" defaultValue={supplier?.contactPerson ?? ""} className={inputClass} />
        </Field>
        <Field label="Email">
          <input name="email" type="email" defaultValue={supplier?.email ?? ""} className={inputClass} />
        </Field>
        <Field label="Telepon">
          <input name="phone" defaultValue={supplier?.phone ?? ""} className={inputClass} />
        </Field>
        <Field label="Alamat">
          <input name="address" defaultValue={supplier?.address ?? ""} className={inputClass} />
        </Field>
        <Field label="Kota">
          <input name="city" defaultValue={supplier?.city ?? ""} className={inputClass} />
        </Field>
        <Field label="Kode Pos">
          <input name="postalCode" defaultValue={supplier?.postalCode ?? ""} className={inputClass} />
        </Field>
        <Field label="No. Lisensi BPOM">
          <input name="licenseNumber" defaultValue={supplier?.licenseNumber ?? ""} className={inputClass} />
        </Field>
        <label className="col-span-2 mt-2 flex items-center gap-2 text-sm">
          <input type="checkbox" name="isActive" defaultChecked={supplier?.isActive ?? true} className="h-4 w-4" />
          Pemasok aktif
        </label>
      </form>
    </Modal>
  );
}
