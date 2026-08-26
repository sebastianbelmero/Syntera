import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { PackagePlus, AlertCircle } from "lucide-react";
import { productApi } from "../../api/catalog";
import { categoryApi, supplierApi } from "../../api/catalog";
import type { ProductDto, DrugClass } from "../../types";
import { formatIDR, formatNumber, formatDate, daysUntil } from "../../lib/format";
import {
  AppGrid,
  Modal,
  Badge,
  type ColumnProps,
} from "../../components";
import { ApiError } from "../../api/client";

const DRUG_CLASS_LABELS: Record<DrugClass, string> = {
  OverTheCounter: "Bebas",
  RestrictedOTC: "Bebas Terbatas",
  PrescriptionOnly: "Keras",
  PharmacyOnly: "Wajib Apotek",
  Narcotic: "Narkotika",
};

/**
 * ProductsPage — migrated to the in-house AppGrid + AppDynamicForm pattern.
 *
 * - enableCrud auto-wires Add/Edit/Delete buttons + a Drawer holding an
 *   AppDynamicForm auto-generated from the ColumnProps[] schema below.
 * - category/supplier lookups are fetched by react-query + injected as
 *   inline arrays (lookup.dataSource accepts both URLs and arrays; we
 *   pass arrays so AppDynamicForm doesn't need to call apiClient again
 *   and the categories/suppliers already in the page's query cache are
 *   reused).
 * - Stock-adjustment flow (POST /api/products/{id}/stock) stays as a
 *   separate Modal because it's a stock-movement record, not
 *   a product upsert — it doesn't fit the AppDynamicForm pattern.
 *   Triggered via customActions(row) button in the grid.
 */
export default function ProductsPage() {
  const queryClient = useQueryClient();
  const [adjusting, setAdjusting] = useState<ProductDto | null>(null);

  // Fetch categories + suppliers for the lookup dropdowns. Reused by
  // both the form (LookupSelect in AppDynamicForm) and... well, just
  // the form. The grid display uses categoryName/supplierName which
  // the API already projects onto the row.
  const categories = useQuery({
    queryKey: ["categories-list"],
    queryFn: () => categoryApi.page({ pageSize: 200 }),
  });
  const suppliers = useQuery({
    queryKey: ["suppliers-list"],
    queryFn: () => supplierApi.page({ pageSize: 200 }),
  });

  const columns: ColumnProps[] = useMemo(() => [
    {
      dataField: "name",
      caption: "Produk",
      editorType: "text",
      validationRules: [{ type: "required", message: "Nama wajib diisi." }],
      cellRender: ({ row }: any) => (
        <div className="min-w-[200px]">
          <p className="font-medium text-[var(--card-foreground)]">{row.name}</p>
          <p className="text-xs text-[var(--muted-foreground)]">
            {row.sku} • {row.genericName ?? "—"}
          </p>
        </div>
      ),
    },
    {
      dataField: "sku",
      caption: "SKU",
      editorType: "text",
      validationRules: [{ type: "required", message: "SKU wajib diisi." }],
      hint: "Stock Keeping Unit — unik.",
      visibleInGrid: false,
    },
    {
      dataField: "barcode",
      caption: "Barcode",
      editorType: "text",
      hint: "EAN-13 / UPC-A. Dipakai untuk scan POS.",
      placeholder: "cth. 8991234567890",
      visibleInGrid: false,
    },
    {
      dataField: "registrationNumber",
      caption: "No. Registrasi BPOM",
      editorType: "text",
      visibleInGrid: false,
    },
    {
      dataField: "genericName",
      caption: "Nama Generik",
      editorType: "text",
      visibleInGrid: false,
    },
    {
      dataField: "brandName",
      caption: "Nama Brand",
      editorType: "text",
      visibleInGrid: false,
    },
    {
      dataField: "manufacturer",
      caption: "Pabrikan",
      editorType: "text",
      visibleInGrid: false,
    },
    {
      dataField: "drugClass",
      caption: "Golongan Obat",
      editorType: "combobox",
      lookup: {
        dataSource: Object.entries(DRUG_CLASS_LABELS).map(([k, v]) => ({ value: k, label: v })),
        valueExpr: "value",
        displayExpr: "label",
      },
      validationRules: [{ type: "required", message: "Golongan wajib dipilih." }],
      cellRender: ({ row }: any) => (
        <Badge variant="outline">{DRUG_CLASS_LABELS[row.drugClass as DrugClass]}</Badge>
      ),
    },
    {
      dataField: "potency",
      caption: "Potensi",
      editorType: "text",
      hint: "cth. '500 mg'",
      visibleInGrid: false,
    },
    {
      dataField: "packSize",
      caption: "Ukuran Kemasan",
      editorType: "text",
      hint: "cth. 'Strip @ 10 kaplet'",
      visibleInGrid: false,
    },
    {
      dataField: "categoryId",
      caption: "Kategori",
      editorType: "combobox",
      lookup: {
        dataSource: categories.data?.items ?? [],
        valueExpr: "id",
        displayExpr: "name",
      },
      validationRules: [{ type: "required", message: "Kategori wajib dipilih." }],
      cellRender: ({ row }: any) => (
        <span className="text-xs">{row.categoryName ?? "—"}</span>
      ),
      hideOnMobile: true,
    },
    {
      dataField: "supplierId",
      caption: "Pemasok",
      editorType: "combobox",
      lookup: {
        dataSource: suppliers.data?.items ?? [],
        valueExpr: "id",
        displayExpr: "name",
      },
      validationRules: [{ type: "required", message: "Pemasok wajib dipilih." }],
      cellRender: ({ row }: any) => (
        <span className="text-xs">{row.supplierName ?? "—"}</span>
      ),
      hideOnMobile: true,
    },
    {
      dataField: "costPrice",
      caption: "Harga Modal",
      editorType: "number",
      dataType: "number",
      validationRules: [{ type: "required", message: "Harga modal wajib diisi." }],
      min: 0,
      step: 100,
      visibleInGrid: false,
    },
    {
      dataField: "sellingPrice",
      caption: "Harga Jual",
      editorType: "number",
      dataType: "number",
      validationRules: [{ type: "required", message: "Harga jual wajib diisi." }],
      min: 0,
      step: 100,
      cellRender: ({ row }: any) => (
        <div className="text-right">
          <span className="font-semibold">{formatIDR(row.sellingPrice)}</span>
          <p className="text-[10px] text-[var(--muted-foreground)]">
            modal {formatIDR(row.costPrice)}
          </p>
        </div>
      ),
    },
    {
      dataField: "discountPrice",
      caption: "Harga Diskon",
      editorType: "number",
      dataType: "number",
      min: 0,
      step: 100,
      hint: "Kosongkan jika tidak ada diskon.",
      placeholder: "cth. 8500",
      visibleInGrid: false,
    },
    {
      dataField: "reorderLevel",
      caption: "Reorder Level",
      editorType: "number",
      dataType: "number",
      min: 0,
      hint: "Ambang batas stok minimum",
      visibleInGrid: false,
    },
    {
      dataField: "expiryDate",
      caption: "Kadaluarsa",
      editorType: "text", // AppDynamicForm's date editorType triggers native date picker
      dataType: "date",
      hideOnMobile: true,
      cellRender: ({ row }: any) => {
        if (!row.expiryDate) return <span className="text-xs">—</span>;
        const d = daysUntil(row.expiryDate);
        const isClose = d !== null && d < 30;
        return (
          <span className={`text-xs ${isClose ? "text-[var(--warning)]" : ""}`}>
            {formatDate(row.expiryDate)}
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
      dataField: "batchNumber",
      caption: "No. Batch",
      editorType: "text",
      visibleInGrid: false,
    },
    {
      dataField: "stock",
      caption: "Stok",
      dataType: "number",
      alignment: "right",
      allowEditing: false,
      visibleInForm: false,
      cellRender: ({ row }: any) => {
        const expired = row.isExpired;
        const low = row.isLowStock;
        return (
          <div className="flex flex-col gap-0.5">
            <span className={low ? "font-bold text-[var(--warning)]" : "font-semibold"}>
              {formatNumber(row.stock)}
            </span>
            <span className="text-[10px] text-[var(--muted-foreground)]">
              reorder: {row.reorderLevel}
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
      dataField: "isActive",
      caption: "Aktif",
      dataType: "boolean",
      editorType: "switch",
      visibleInGrid: false,
    },
    {
      dataField: "createdAt",
      caption: "Dibuat",
      dataType: "date",
      visibleInGrid: false,
      visibleInForm: false,
      allowFiltering: false,
      allowEditing: false,
    },
  ], [categories.data, suppliers.data]);

  const handleStockSaved = () => {
    void queryClient.invalidateQueries({ queryKey: ["products"] });
    void queryClient.invalidateQueries({ queryKey: ["dashboard-summary"] });
    setAdjusting(null);
  };

  return (
    <div className="flex flex-col gap-4">
      <header>
        <h2 className="text-2xl font-bold tracking-tight text-[var(--card-foreground)]">
          Produk
        </h2>
        <p className="text-sm text-[var(--muted-foreground)]">
          Katalog obat, suplemen, dan produk kesehatan lainnya.
        </p>
      </header>

      <AppGrid
        apiEndpoint="/api/products/grid"
        enableCrud
        allowAdd
        allowEdit
        allowDelete={false}
        enableFiltering
        enableSorting
        enableColumnChooser
        enablePagination
        enableStatePersistence
        persistenceKey="products-grid"
        globalFilterFields={["name", "sku", "barcode", "genericName", "brandName", "manufacturer", "categoryName", "supplierName"]}
        title="Daftar Produk"
        pageSize={10}
        dynamicColumns={columns}
        customActions={(row: any) => (
          <button
            type="button"
            onClick={() => setAdjusting(row)}
            className="rounded p-1.5 text-[var(--primary)] transition-colors hover:bg-[var(--primary)]/10"
            title="Sesuaikan stok"
            aria-label="Sesuaikan stok"
          >
            <PackagePlus size={14} />
          </button>
        )}
      />

      {adjusting && (
        <StockAdjustModal
          product={adjusting}
          onClose={() => setAdjusting(null)}
          onSaved={handleStockSaved}
        />
      )}
    </div>
  );
}

// ── Stock adjustment modal ──────────────────────────────────
// Uses the shared in-house Modal (focus-trapped + animated) with an inline
// form. Not migrated to AppDynamicForm because the submit goes to
// /api/products/{id}/stock (a movement record), not /api/products/{id}
// (a product upsert) — different DTO shape, different validation,
// different semantics.
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
      isOpen
      onClose={onClose}
      title="Sesuaikan Stok"
      width="w-full sm:max-w-md"
      footer={
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="app-grid-btn"
            disabled={submitting}
          >
            Batal
          </button>
          <button
            type="submit"
            form="stock-form"
            disabled={submitting}
            className="app-grid-btn app-grid-btn-primary"
          >
            {submitting ? "Menyimpan…" : "Simpan"}
          </button>
        </div>
      }
    >
      <p className="mb-4 text-xs text-[var(--muted-foreground)]">
        {product.name} — SKU {product.sku} — Stok saat ini: {formatNumber(product.stock)}
      </p>
      <form id="stock-form" onSubmit={handleSubmit} className="space-y-3">
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-bold text-[var(--card-foreground)]">
            Selisih Quantity <span className="text-[var(--danger)]">*</span>
          </label>
          <input
            name="quantity"
            type="number"
            defaultValue={0}
            className="app-grid-filter-input"
            required
          />
          <span className="text-xs italic text-[var(--muted-foreground)]">
            Positif = masuk, negatif = keluar.
          </span>
        </div>
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-bold text-[var(--card-foreground)]">
            Catatan
          </label>
          <textarea
            name="note"
            rows={3}
            className="app-grid-filter-input"
            placeholder="cth. PO-2026-001, retur, kerusakan…"
          />
        </div>
      </form>
    </Modal>
  );
}
