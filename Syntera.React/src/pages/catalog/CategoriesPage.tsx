import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Pencil, Trash2, Layers } from "lucide-react";
import { categoryApi } from "../../api/catalog";
import type { CategoryDto, CategoryUpsertDto } from "../../types";
import { formatDate } from "../../lib/format";
import { AppGrid, type AppGridColumn } from "../../components/AppGrid";
import {
  Modal,
  Field,
  inputClass,
  btnPrimary,
  btnGhost,
  ConfirmDialog,
} from "../../components/Modal";
import { ApiError } from "../../api/client";

export default function CategoriesPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<CategoryDto | null>(null);
  const [deleting, setDeleting] = useState<CategoryDto | null>(null);

  // Fetch the full category list once so the form's parent
  // <select> can be populated AND so the expandable detail row
  // can render the sub-categories grid (filtered client-side
  // by parentId === row.id). Reused queryKey is invalidated on
  // save so new categories appear in the picker immediately.
  const parentsQuery = useQuery({
    queryKey: ["categories-list"],
    queryFn: () => categoryApi.page({ pageSize: 200 }),
  });

  const allCategories = parentsQuery.data?.items ?? [];

  const columns: AppGridColumn<CategoryDto>[] = [
    {
      key: "name",
      header: "Nama",
      sortable: true,
      sortAccessor: (c) => c.name,
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
      hideOnMobile: true,
      render: (c) => (
        <span className="text-xs text-[var(--muted-foreground)]">
          {c.description ?? "—"}
        </span>
      ),
    },
    {
      key: "parent",
      header: "Induk",
      hideOnMobile: true,
      render: (c) => (
        <span className="text-xs">{c.parentName ?? "—"}</span>
      ),
    },
    {
      key: "products",
      header: "Produk",
      align: "right",
      sortable: true,
      sortAccessor: (c) => c.productCount,
      render: (c) => <span className="text-xs">{c.productCount}</span>,
    },
    {
      key: "created",
      header: "Dibuat",
      hideOnMobile: true,
      sortable: true,
      sortAccessor: (c) => c.createdAt,
      render: (c) => <span className="text-xs">{formatDate(c.createdAt)}</span>,
    },
    {
      key: "actions",
      header: "",
      align: "right",
      render: (c) => (
        <div className="flex items-center justify-end gap-1">
          <button
            type="button"
            onClick={() => setEditing(c)}
            className="rounded-md p-1.5 text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--primary)]"
            aria-label={`Edit kategori ${c.name}`}
            title="Edit kategori"
          >
            <Pencil size={16} />
          </button>
          <button
            type="button"
            onClick={() => setDeleting(c)}
            className="rounded-md p-1.5 text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--danger)]"
            aria-label={`Hapus kategori ${c.name}`}
            title="Hapus kategori"
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
            Klik baris untuk melihat sub-kategori.
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

      <AppGrid<CategoryDto>
        columns={columns}
        rowKey={(c) => c.id}
        load={async ({ page, pageSize, search }) => {
          const res = await categoryApi.page({ page, pageSize, search });
          return { items: res.items, total: res.total, totalPages: res.totalPages };
        }}
        expandable={{
          // AppGrid-in-AppGrid: the recursive nesting pattern. The
          // detail panel renders a SECOND <AppGrid> whose rows are
          // the children of the parent category. Because that inner
          // grid ALSO accepts an `expandable` prop, the nesting can
          // go arbitrarily deep (grandchildren, great-grandchildren,
          // …) — true table-in-table recursion.
          renderDetail: (parent) => (
            <SubCategoryGrid
              parent={parent}
              allCategories={allCategories}
              onEdit={(c) => setEditing(c)}
              onDelete={(c) => setDeleting(c)}
            />
          ),
          // Only show the expand chevron when this category
          // actually has children — keeps the row tidy.
          detailLabel: (c) => `sub-kategori ${c.name}`,
          lazy: false,
        }}
      />

      {(createOpen || editing) && (
        <CategoryFormModal
          open
          category={editing}
          parents={allCategories.filter((c) => c.id !== editing?.id)}
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

      <ConfirmDialog
        open={!!deleting}
        title="Konfirmasi Hapus"
        description={deleting ? `Hapus kategori "${deleting.name}"? Tindakan ini tidak dapat dibatalkan.` : undefined}
        confirmLabel="Hapus"
        onClose={() => setDeleting(null)}
        onConfirm={async () => {
          if (!deleting) return;
          try {
            await categoryApi.remove(deleting.id);
            toast.success("Kategori dihapus.");
            void queryClient.invalidateQueries({ queryKey: ["categories"] });
            void queryClient.invalidateQueries({ queryKey: ["categories-list"] });
            setDeleting(null);
          } catch (err) {
            toast.error((err as ApiError).message ?? "Gagal menghapus.");
          }
        }}
      />
    </div>
  );
}

/**
 * SubCategoryGrid — the inner AppGrid rendered inside the
 * expanded row of the parent grid. Demonstrates the recursive
 * "AppGrid-in-AppGrid" pattern: it accepts the full category
 * list and filters to children of `parent.id`, then renders
 * those children with the same column shape as the outer grid.
 *
 * Because this is itself an AppGrid, ITS rows can also be
 * expandable — grandchildren, great-grandchildren, etc. —
 * demonstrating true table-in-table recursion.
 */
function SubCategoryGrid({
  parent,
  allCategories,
  onEdit,
  onDelete,
}: {
  parent: CategoryDto;
  allCategories: CategoryDto[];
  onEdit: (c: CategoryDto) => void;
  onDelete: (c: CategoryDto) => void;
}) {
  const children = allCategories.filter((c) => c.parentId === parent.id);

  const childColumns: AppGridColumn<CategoryDto>[] = [
    {
      key: "name",
      header: "Sub-Kategori",
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
      key: "products",
      header: "Produk",
      align: "right",
      render: (c) => <span className="text-xs">{c.productCount}</span>,
    },
    {
      key: "actions",
      header: "",
      align: "right",
      render: (c) => (
        <div className="flex items-center justify-end gap-1">
          <button
            type="button"
            onClick={() => onEdit(c)}
            className="rounded-md p-1.5 text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--primary)]"
            aria-label={`Edit sub-kategori ${c.name}`}
            title="Edit sub-kategori"
          >
            <Pencil size={14} />
          </button>
          <button
            type="button"
            onClick={() => onDelete(c)}
            className="rounded-md p-1.5 text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--danger)]"
            aria-label={`Hapus sub-kategori ${c.name}`}
            title="Hapus sub-kategori"
          >
            <Trash2 size={14} />
          </button>
        </div>
      ),
    },
  ];

  if (children.length === 0) {
    return (
      <div className="flex items-center gap-2 rounded-lg border border-dashed border-[var(--border)] bg-[var(--card)] p-3 text-xs text-[var(--muted-foreground)]">
        <Layers size={14} />
        <span>Kategori ini tidak memiliki sub-kategori.</span>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2 text-xs text-[var(--muted-foreground)]">
        <Layers size={12} />
        <span>{children.length} sub-kategori dari <strong>{parent.name}</strong></span>
      </div>
      {/* Recursion — this is the AppGrid-in-AppGrid pattern: a
          full AppGrid (with its own pagination / search / sort /
          expandable) rendered inside an expanded row of the
          parent AppGrid. Children that themselves have children
          will show their own expand chevron. */}
      <AppGrid<CategoryDto>
        columns={childColumns}
        rowKey={(c) => c.id}
        initialPageSize={5}
        load={async () => {
          // No async fetch — children are already in memory. The
          // AppGrid contract requires a `load` function returning
          // a PagedResult, so we adapt the in-memory list to the
          // async interface.
          return { items: children, total: children.length, totalPages: 1 };
        }}
        expandable={{
          renderDetail: (sub) => (
            <SubCategoryGrid
              parent={sub}
              allCategories={allCategories}
              onEdit={onEdit}
              onDelete={onDelete}
            />
          ),
          detailLabel: (c) => `sub-kategori ${c.name}`,
        }}
        emptyMessage="Tidak ada sub-kategori."
      />
    </div>
  );
}

function CategoryFormModal({
  open,
  category,
  parents,
  onClose,
  onSaved,
}: {
  open: boolean;
  category: CategoryDto | null;
  parents: { id: string; name: string }[];
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
        <Field label="Kategori Induk" hint="Kosongkan untuk kategori tingkat atas.">
          <select
            name="parentId"
            defaultValue={category?.parentId ?? ""}
            className={inputClass}
          >
            <option value="">— Tanpa induk —</option>
            {parents.map((p) => (
              <option key={p.id} value={p.id}>{p.name}</option>
            ))}
          </select>
        </Field>
      </form>
    </Modal>
  );
}
