import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Pencil, PackagePlus, AlertCircle } from "lucide-react";
import { productApi } from "../../api/catalog";
import { categoryApi, supplierApi } from "../../api/catalog";
import type { ProductDto, ProductUpsertDto, DrugClass } from "../../types";
import { formatIDR, formatNumber, formatDate, daysUntil } from "../../lib/format";
import { DataTable, type DataTableColumn } from "../../components/DataTable";
import { Modal, Field, inputClass, btnPrimary, btnGhost } from "../../components/Modal";
import { ApiError } from "../../api/client";

const DRUG_CLASS_LABELS: Record<DrugClass, string> = {
  OverTheCounter: "Bebas",
  RestrictedOTC: "Bebas Terbatas",
  PrescriptionOnly: "Keras",
  PharmacyOnly: "Wajib Apotek",
  Narcotic: "Narkotika",
};

export default function ProductsPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<ProductDto | null>(null);
  const [adjusting, setAdjusting] = useState<ProductDto | null>(null);

  const categories = useQuery({
    queryKey: ["categories-list"],
    queryFn: () => categoryApi.page({ pageSize: 200 }),
  });
  const suppliers = useQuery({
    queryKey: ["suppliers-list"],
    queryFn: () => supplierApi.page({ pageSize: 200 }),
  });

  const columns: DataTableColumn<ProductDto>[] = [
    {
      key: "name",
      header: "Produk",
      render: (p) => (
        <div className="min-w-[200px]">
          <p className="font-medium">{p.name}</p>
          <p className="text-xs text-[var(--muted-foreground)]">
            {p.sku} • {p.genericName ?? "—"}
          </p>
        </div>
      ),
    },
    {
      key: "category",
      header: "Kategori",
      render: (p) => <span className="text-xs">{p.categoryName}</span>,
    },
    {
      key: "supplier",
      header: "Pemasok",
      render: (p) => <span className="text-xs">{p.supplierName}</span>,
    },
    {
      key: "drugClass",
      header: "Golongan",
      render: (p) => (
        <span className="rounded-full bg-[var(--surface)] px-2 py-1 text-xs">
          {DRUG_CLASS_LABELS[p.drugClass]}
        </span>
      ),
    },
    {
      key: "stock",
      header: "Stok",
      render: (p) => {
        const expired = p.isExpired;
        const low = p.isLowStock;
        return (
          <div className="flex flex-col gap-0.5">
            <span className={low ? "font-bold text-[var(--warning)]" : "font-semibold"}>
              {formatNumber(p.stock)}
            </span>
            <span className="text-[10px] text-[var(--muted-foreground)]">
              reorder: {p.reorderLevel}
            </span>
            {expired && (
              <span className="flex items-center gap-1 text-[10px] font-semibold text-[var(--danger)]">
                <AlertCircle size={10} /> Kadaluarsa
              </span>
            )}
          </div>
        );
      },
    },
    {
      key: "expiry",
      header: "Kadaluarsa",
      render: (p) => {
        if (!p.expiryDate) return <span className="text-xs">—</span>;
        const d = daysUntil(p.expiryDate);
        const isClose = d !== null && d < 30;
        return (
          <span className={`text-xs ${isClose ? "text-[var(--warning)]" : ""}`}>
            {formatDate(p.expiryDate)}
            {d !== null && (
              <span className="block text-[10px] text-[var(--muted-foreground)]">
                {d > 0 ? `${d} hari lagi` : "sudah lewat"}
              </span>
            )}
          </span>
        );
      },
    },
    {
      key: "price",
      header: "Harga",
      render: (p) => (
        <div className="text-right">
          <span className="font-semibold">{formatIDR(p.sellingPrice)}</span>
          <p className="text-[10px] text-[var(--muted-foreground)]">
            modal {formatIDR(p.costPrice)}
          </p>
        </div>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (p) => (
        <div className="flex items-center justify-end gap-1">
          <button
            type="button"
            onClick={() => setAdjusting(p)}
            className="rounded-md p-1.5 text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--primary)]"
            title="Sesuaikan stok"
          >
            <PackagePlus size={16} />
          </button>
          <button
            type="button"
            onClick={() => setEditing(p)}
            className="rounded-md p-1.5 text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--primary)]"
            title="Edit produk"
          >
            <Pencil size={16} />
          </button>
        </div>
      ),
    },
  ];

  const handleSaved = () => {
    void queryClient.invalidateQueries({ queryKey: ["products"] });
    void queryClient.invalidateQueries({ queryKey: ["dashboard-summary"] });
    setCreateOpen(false);
    setEditing(null);
  };

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight">Produk</h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Katalog obat, suplemen, dan produk kesehatan lainnya.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setCreateOpen(true)}
          className={`${btnPrimary} flex items-center gap-2`}
        >
          <Plus size={16} /> Produk Baru
        </button>
      </header>

      <DataTable<ProductDto>
        columns={columns}
        rowKey={(p) => p.id}
        load={async ({ page, pageSize, search }) => {
          const res = await productApi.search({ page, pageSize, search });
          return { items: res.items, total: res.total, totalPages: res.totalPages };
        }}
        emptyMessage="Belum ada produk. Klik 'Produk Baru' untuk menambahkan."
      />

      {(createOpen || editing) && (
        <ProductFormModal
          open
          product={editing}
          categories={categories.data?.items ?? []}
          suppliers={suppliers.data?.items ?? []}
          onClose={() => {
            setCreateOpen(false);
            setEditing(null);
          }}
          onSaved={handleSaved}
        />
      )}

      {adjusting && (
        <StockAdjustModal
          product={adjusting}
          onClose={() => setAdjusting(null)}
          onSaved={() => {
            void queryClient.invalidateQueries({ queryKey: ["products"] });
            setAdjusting(null);
          }}
        />
      )}
    </div>
  );
}

// ── Product form (create / update) ───────────────────────────
function ProductFormModal({
  open,
  product,
  categories,
  suppliers,
  onClose,
  onSaved,
}: {
  open: boolean;
  product: ProductDto | null;
  categories: { id: string; name: string }[];
  suppliers: { id: string; name: string }[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const isEdit = !!product;
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      const fd = new FormData(e.currentTarget);
      const dto: ProductUpsertDto = {
        name: String(fd.get("name") ?? ""),
        sku: String(fd.get("sku") ?? ""),
        barcode: (fd.get("barcode") as string) || null,
        registrationNumber: (fd.get("registrationNumber") as string) || null,
        genericName: (fd.get("genericName") as string) || null,
        brandName: (fd.get("brandName") as string) || null,
        manufacturer: (fd.get("manufacturer") as string) || null,
        drugClass: fd.get("drugClass") as DrugClass,
        potency: (fd.get("potency") as string) || null,
        packSize: (fd.get("packSize") as string) || null,
        costPrice: Number(fd.get("costPrice") ?? 0),
        sellingPrice: Number(fd.get("sellingPrice") ?? 0),
        discountPrice: fd.get("discountPrice")
          ? Number(fd.get("discountPrice"))
          : null,
        reorderLevel: Number(fd.get("reorderLevel") ?? 0),
        expiryDate: (fd.get("expiryDate") as string)
          ? new Date(fd.get("expiryDate") as string).toISOString()
          : null,
        batchNumber: (fd.get("batchNumber") as string) || null,
        isActive: fd.get("isActive") === "on",
        categoryId: String(fd.get("categoryId") ?? ""),
        supplierId: String(fd.get("supplierId") ?? ""),
      };
      if (isEdit && product) {
        await productApi.update(product.id, dto);
        toast.success("Produk diperbarui.");
      } else {
        await productApi.create(dto);
        toast.success("Produk ditambahkan.");
      }
      onSaved();
    } catch (err) {
      const apiErr = err as ApiError;
      toast.error(apiErr.message ?? "Gagal menyimpan produk.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={isEdit ? "Edit Produk" : "Tambah Produk"}
      description="Isi data sesuai label kemasan dan nomor registrasi BPOM."
      size="xl"
      footer={
        <>
          <button type="button" className={btnGhost} onClick={onClose}>
            Batal
          </button>
          <button
            type="submit"
            form="product-form"
            disabled={submitting}
            className={btnPrimary}
          >
            {submitting ? "Menyimpan…" : "Simpan"}
          </button>
        </>
      }
    >
      <form id="product-form" onSubmit={handleSubmit} className="grid grid-cols-2 gap-3">
        <Field label="Nama Produk" required>
          <input
            name="name"
            defaultValue={product?.name}
            className={inputClass}
            required
          />
        </Field>
        <Field label="SKU" required hint="Stock Keeping Unit — unik.">
          <input
            name="sku"
            defaultValue={product?.sku}
            className={inputClass}
            required
          />
        </Field>
        <Field label="Nama Generik">
          <input
            name="genericName"
            defaultValue={product?.genericName ?? ""}
            className={inputClass}
          />
        </Field>
        <Field label="Nama Brand">
          <input
            name="brandName"
            defaultValue={product?.brandName ?? ""}
            className={inputClass}
          />
        </Field>
        <Field label="Pabrikan">
          <input
            name="manufacturer"
            defaultValue={product?.manufacturer ?? ""}
            className={inputClass}
          />
        </Field>
        <Field label="No. Registrasi BPOM">
          <input
            name="registrationNumber"
            defaultValue={product?.registrationNumber ?? ""}
            className={inputClass}
          />
        </Field>
        <Field label="Golongan Obat">
          <select
            name="drugClass"
            defaultValue={product?.drugClass ?? "OverTheCounter"}
            className={inputClass}
          >
            {Object.entries(DRUG_CLASS_LABELS).map(([k, v]) => (
              <option key={k} value={k}>{v}</option>
            ))}
          </select>
        </Field>
        <Field label="Potensi / Kekuatan" hint="cth. '500 mg'">
          <input
            name="potency"
            defaultValue={product?.potency ?? ""}
            className={inputClass}
          />
        </Field>
        <Field label="Ukuran Kemasan" hint="cth. 'Strip @ 10 kaplet'">
          <input
            name="packSize"
            defaultValue={product?.packSize ?? ""}
            className={inputClass}
          />
        </Field>
        <Field label="No. Batch">
          <input
            name="batchNumber"
            defaultValue={product?.batchNumber ?? ""}
            className={inputClass}
          />
        </Field>
        <Field label="Harga Modal (Rp)" required>
          <input
            name="costPrice"
            type="number"
            min={0}
            step="100"
            defaultValue={product?.costPrice ?? 0}
            className={inputClass}
            required
          />
        </Field>
        <Field label="Harga Jual (Rp)" required>
          <input
            name="sellingPrice"
            type="number"
            min={0}
            step="100"
            defaultValue={product?.sellingPrice ?? 0}
            className={inputClass}
            required
          />
        </Field>
        <Field label="Reorder Level" hint="Ambang batas stok minimum">
          <input
            name="reorderLevel"
            type="number"
            min={0}
            defaultValue={product?.reorderLevel ?? 10}
            className={inputClass}
          />
        </Field>
        <Field label="Tanggal Kadaluarsa">
          <input
            name="expiryDate"
            type="date"
            defaultValue={
              product?.expiryDate
                ? new Date(product.expiryDate).toISOString().split("T")[0]
                : ""
            }
            className={inputClass}
          />
        </Field>
        <Field label="Kategori" required>
          <select
            name="categoryId"
            defaultValue={product?.categoryId ?? ""}
            className={inputClass}
            required
          >
            <option value="">Pilih kategori…</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
        </Field>
        <Field label="Pemasok" required>
          <select
            name="supplierId"
            defaultValue={product?.supplierId ?? ""}
            className={inputClass}
            required
          >
            <option value="">Pilih pemasok…</option>
            {suppliers.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
        </Field>
        <label className="col-span-2 mt-2 flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            name="isActive"
            defaultChecked={product?.isActive ?? true}
            className="h-4 w-4"
          />
          Produk aktif (ditampilkan di katalog)
        </label>
      </form>
    </Modal>
  );
}

// ── Stock adjustment modal ──────────────────────────────────
function StockAdjustModal({
  product,
  onClose,
  onSaved,
}: {
  product: ProductDto;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const fd = new FormData(e.currentTarget);
    const qty = Number(fd.get("quantity") ?? 0);
    const note = String(fd.get("note") ?? "");
    if (!qty) {
      toast.error("Quantity tidak boleh 0.");
      return;
    }
    setSubmitting(true);
    try {
      await productApi.adjustStock(product.id, { quantity: qty, note });
      toast.success(`Stok ${product.name} disesuaikan ${qty > 0 ? "+" : ""}${qty}.`);
      onSaved();
    } catch (err) {
      const apiErr = err as ApiError;
      toast.error(apiErr.message ?? "Gagal menyesuaikan stok.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="Sesuaikan Stok"
      description={`${product.name} — SKU ${product.sku} — Stok saat ini: ${formatNumber(product.stock)}`}
      size="md"
      footer={
        <>
          <button type="button" className={btnGhost} onClick={onClose}>
            Batal
          </button>
          <button
            type="submit"
            form="stock-form"
            disabled={submitting}
            className={btnPrimary}
          >
            {submitting ? "Menyimpan…" : "Simpan"}
          </button>
        </>
      }
    >
      <form id="stock-form" onSubmit={handleSubmit} className="space-y-3">
        <Field label="Selisih Quantity" required hint="Positif = masuk, negatif = keluar.">
          <input
            name="quantity"
            type="number"
            defaultValue={0}
            className={inputClass}
            required
          />
        </Field>
        <Field label="Catatan">
          <textarea
            name="note"
            rows={3}
            className={inputClass}
            placeholder="cth. PO-2026-001, retur, kerusakan…"
          />
        </Field>
      </form>
    </Modal>
  );
}
