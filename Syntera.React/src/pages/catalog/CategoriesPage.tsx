import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Pencil, Trash2 } from "lucide-react";
import { categoryApi } from "../../api/catalog";
import type { CategoryDto, CategoryUpsertDto } from "../../types";
import { formatDate } from "../../lib/format";
import { DataTable, type DataTableColumn } from "../../components/DataTable";
import { Modal, Field, inputClass, btnPrimary, btnGhost } from "../../components/Modal";
import { ApiError } from "../../api/client";

export default function CategoriesPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<CategoryDto | null>(null);
  const [deleting, setDeleting] = useState<CategoryDto | null>(null);

  const columns: DataTableColumn<CategoryDto>[] = [
    {
      key: "name",
      header: "Nama",
      render: (c) => (
        <div>
          <p className="font-medium">{c.name}</p>
          <p className="text-xs text-[var(--muted-foreground)]">/{c.slug}</p>
        </div>
      ),
    },
    {
      key: "description",
      header: "Deskripsi",
      render: (c) => (
        <span className="text-xs text-[var(--muted-foreground)]">
          {c.description ?? "—"}
        </span>
      ),
    },
    {
      key: "parent",
      header: "Induk",
      render: (c) => (
        <span className="text-xs">{c.parentName ?? "—"}</span>
      ),
    },
    {
      key: "products",
      header: "Produk",
      render: (c) => <span className="text-xs">{c.productCount}</span>,
    },
    {
      key: "created",
      header: "Dibuat",
      render: (c) => <span className="text-xs">{formatDate(c.createdAt)}</span>,
    },
    {
      key: "actions",
      header: "",
      render: (c) => (
        <div className="flex items-center justify-end gap-1">
          <button
            type="button"
            onClick={() => setEditing(c)}
            className="rounded-md p-1.5 text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--primary)]"
          >
            <Pencil size={16} />
          </button>
          <button
            type="button"
            onClick={() => setDeleting(c)}
            className="rounded-md p-1.5 text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--danger)]"
          >
            <Trash2 size={16} />
          </button>
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight">Kategori</h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Pengelompokan produk berbasis klasifikasi Kemenkes.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setCreateOpen(true)}
          className={`${btnPrimary} flex items-center gap-2`}
        >
          <Plus size={16} /> Kategori Baru
        </button>
      </header>

      <DataTable<CategoryDto>
        columns={columns}
        rowKey={(c) => c.id}
        load={async ({ page, pageSize, search }) => {
          const res = await categoryApi.page({ page, pageSize, search });
          return { items: res.items, total: res.total, totalPages: res.totalPages };
        }}
      />

      {(createOpen || editing) && (
        <CategoryFormModal
          open
          category={editing}
          onClose={() => {
            setCreateOpen(false);
            setEditing(null);
          }}
          onSaved={() => {
            void queryClient.invalidateQueries({ queryKey: ["categories"] });
            void queryClient.invalidateQueries({ queryKey: ["categories-list"] });
            setCreateOpen(false);
            setEditing(null);
          }}
        />
      )}

      {deleting && (
        <ConfirmDeleteModal
          name={deleting.name}
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            try {
              await categoryApi.remove(deleting.id);
              toast.success("Kategori dihapus.");
              void queryClient.invalidateQueries({ queryKey: ["categories"] });
            } catch (err) {
              toast.error((err as ApiError).message ?? "Gagal menghapus.");
            } finally {
              setDeleting(null);
            }
          }}
        />
      )}
    </div>
  );
}

function CategoryFormModal({
  open,
  category,
  onClose,
  onSaved,
}: {
  open: boolean;
  category: CategoryDto | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [submitting, setSubmitting] = useState(false);
  const isEdit = !!category;

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      const fd = new FormData(e.currentTarget);
      const dto: CategoryUpsertDto = {
        name: String(fd.get("name") ?? ""),
        description: (fd.get("description") as string) || null,
        parentId: (fd.get("parentId") as string) || null,
      };
      if (isEdit && category) {
        await categoryApi.update(category.id, dto);
        toast.success("Kategori diperbarui.");
      } else {
        await categoryApi.create(dto);
        toast.success("Kategori ditambahkan.");
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
      title={isEdit ? "Edit Kategori" : "Tambah Kategori"}
      footer={
        <>
          <button type="button" className={btnGhost} onClick={onClose}>Batal</button>
          <button type="submit" form="cat-form" disabled={submitting} className={btnPrimary}>
            {submitting ? "Menyimpan…" : "Simpan"}
          </button>
        </>
      }
    >
      <form id="cat-form" onSubmit={handleSubmit} className="space-y-3">
        <Field label="Nama Kategori" required>
          <input
            name="name"
            defaultValue={category?.name}
            className={inputClass}
            required
          />
        </Field>
        <Field label="Deskripsi">
          <textarea
            name="description"
            rows={3}
            defaultValue={category?.description ?? ""}
            className={inputClass}
          />
        </Field>
      </form>
    </Modal>
  );
}

function ConfirmDeleteModal({
  name,
  onClose,
  onConfirm,
}: {
  name: string;
  onClose: () => void;
  onConfirm: () => Promise<void>;
}) {
  const [busy, setBusy] = useState(false);
  return (
    <Modal
      open
      onClose={onClose}
      title="Konfirmasi Hapus"
      description={`Hapus kategori "${name}"? Tindakan ini tidak dapat dibatalkan.`}
      size="sm"
      footer={
        <>
          <button type="button" className={btnGhost} onClick={onClose}>Batal</button>
          <button
            type="button"
            disabled={busy}
            onClick={async () => {
              setBusy(true);
              await onConfirm();
              setBusy(false);
              onClose();
            }}
            className="rounded-lg bg-[var(--danger)] px-4 py-2 text-sm font-semibold text-white transition hover:bg-[var(--danger-hover)] disabled:opacity-60"
          >
            {busy ? "Menghapus…" : "Hapus"}
          </button>
        </>
      }
    >
      <p className="text-sm text-[var(--muted-foreground)]">
        Soft-delete — data tetap tersimpan untuk keperluan audit.
      </p>
    </Modal>
  );
}
