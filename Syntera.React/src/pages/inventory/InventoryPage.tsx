import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus } from "lucide-react";
import { inventoryApi } from "../../api/operations";
import { productApi } from "../../api/catalog";
import type { InventoryMovementType } from "../../types";
import { formatDateTime } from "../../lib/format";
import {
  AppGrid,
  type ColumnProps,
} from "../../kalventis/ui";
import { Modal } from "../../kalventis/ui";
import { Badge } from "../../kalventis/ui";
import { ApiError } from "../../api/client";

const TYPE_LABEL: Record<InventoryMovementType, string> = {
  Inbound: "Masuk",
  Outbound: "Keluar",
  Adjustment: "Penyesuaian",
  Return: "Retur",
  Damage: "Kerusakan",
};

const TYPE_VARIANT: Record<InventoryMovementType, "success" | "info" | "warning" | "secondary" | "destructive"> = {
  Inbound: "success",
  Outbound: "info",
  Adjustment: "warning",
  Return: "secondary",
  Damage: "destructive",
};

/**
 * InventoryPage — migrated to kalventis AppGrid pattern for display.
 * The record-movement form stays as a separate Modal (not AppDynamicForm)
 * because:
 *   1. The submit goes to /api/inventory (a movement record, not an
 *      inventory upsert — different DTO).
 *   2. Quantity auto-signing logic (Inbound/Adjustment = +, others = -)
 *      needs custom transform between UI and API.
 *   3. The product picker fetches a fresh product list lazily on modal
 *      open (was previously a render-body fetch — moved to useEffect
 *      to avoid StrictMode double-fire + render flood).
 *
 * The grid itself uses the new pattern: enableFiltering + enableColumnChooser
 * + globalFilterFields + DevExtreme server-side protocol via
 * /api/inventory/grid. enableCrud is OFF because movements are append-only
 * (no edit/delete) — the API doesn't support either.
 */
export default function InventoryPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);

  // Fetch product list for the record-movement form's product picker.
  // Loaded eagerly so the modal opens instantly with options populated
  // (was lazy-loaded in the modal's useEffect in the previous version).
  const productsQuery = useQuery({
    queryKey: ["products-list-for-picker"],
    queryFn: () => productApi.search({ pageSize: 200 }),
  });

  const columns: ColumnProps[] = useMemo(() => [
    {
      dataField: "createdAt",
      caption: "Waktu",
      dataType: "date",
      hideOnMobile: true,
      cellRender: ({ row }: any) => (
        <span className="text-xs">{formatDateTime(row.createdAt)}</span>
      ),
    },
    {
      dataField: "productName",
      caption: "Produk",
      allowEditing: false,
      visibleInForm: false,
      cellRender: ({ row }: any) => (
        <div>
          <p className="font-medium text-[var(--card-foreground)]">{row.productName}</p>
          <p className="text-xs text-[var(--muted-foreground)]">{row.productSku}</p>
        </div>
      ),
    },
    {
      dataField: "type",
      caption: "Jenis",
      cellRender: ({ row }: any) => (
        <Badge variant={TYPE_VARIANT[row.type as InventoryMovementType]}>
          {TYPE_LABEL[row.type as InventoryMovementType]}
        </Badge>
      ),
    },
    {
      dataField: "quantity",
      caption: "Quantity",
      dataType: "number",
      alignment: "right",
      cellRender: ({ row }: any) => (
        <span className={`font-semibold ${row.quantity > 0 ? "text-[var(--success)]" : "text-[var(--danger)]"}`}>
          {row.quantity > 0 ? "+" : ""}{row.quantity}
        </span>
      ),
    },
    {
      dataField: "balanceAfter",
      caption: "Saldo",
      dataType: "number",
      alignment: "right",
      hideOnMobile: true,
      cellRender: ({ row }: any) => (
        <span className="font-medium">{row.balanceAfter}</span>
      ),
    },
    {
      dataField: "reference",
      caption: "Referensi",
      hideOnMobile: true,
      cellRender: ({ row }: any) => (
        <span className="text-xs text-[var(--muted-foreground)]">
          {row.reference ?? "—"}
        </span>
      ),
    },
    {
      dataField: "note",
      caption: "Catatan",
      hideOnMobile: true,
      cellRender: ({ row }: any) => (
        <span className="text-xs text-[var(--muted-foreground)]">
          {row.note ?? "—"}
        </span>
      ),
    },
  ], []);

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight text-[var(--card-foreground)]">
            Persediaan
          </h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Ledger pergerakan stok — single source of truth untuk
            on-hand quantity.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setCreateOpen(true)}
          className="app-grid-btn app-grid-btn-primary"
        >
          <Plus size={14} className="mr-1.5" /> Catat Pergerakan
        </button>
      </header>

      <AppGrid
        apiEndpoint="/api/inventory/grid"
        enableFiltering
        enableSorting
        enableColumnChooser
        enablePagination
        enableStatePersistence
        persistenceKey="inventory-grid"
        globalFilterFields={["productName", "productSku", "reference", "note"]}
        title="Riwayat Pergerakan Stok"
        pageSize={10}
        dynamicColumns={columns}
      />

      {createOpen && (
        <InventoryFormModal
          isOpen
          products={productsQuery.data?.items ?? []}
          productsLoading={productsQuery.isLoading}
          onClose={() => setCreateOpen(false)}
          onSaved={() => {
            void queryClient.invalidateQueries({ queryKey: ["inventory"] });
            void queryClient.invalidateQueries({ queryKey: ["products"] });
            void queryClient.invalidateQueries({ queryKey: ["dashboard-summary"] });
            setCreateOpen(false);
          }}
        />
      )}
    </div>
  );
}

// ── Record movement form modal ─────────────────────────────
// Uses kalventis Modal (focus-trapped + animated) with an inline form.
// Not migrated to AppDynamicForm because the submit semantics differ
// from a standard CRUD upsert (signed quantity + endpoint is
// /api/inventory, not /api/inventory/{id}).
function InventoryFormModal({
  isOpen,
  products,
  productsLoading,
  onClose,
  onSaved,
}: {
  isOpen: boolean;
  products: { id: string; name: string; sku: string }[];
  productsLoading: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      const fd = new FormData(e.currentTarget);
      const productId = String(fd.get("productId") ?? "");
      const type = fd.get("type") as InventoryMovementType;
      const quantity = Number(fd.get("quantity") ?? 0);
      const reference = (fd.get("reference") as string) || null;
      const note = (fd.get("note") as string) || null;
      if (!productId) {
        toast.error("Pilih produk terlebih dahulu.");
        setSubmitting(false);
        return;
      }
      // For Outbound / Return / Damage, quantity is negative.
      const signedQty = type === "Inbound" || type === "Adjustment" ? quantity : -Math.abs(quantity);
      await inventoryApi.record({ productId, type, quantity: signedQty, reference, note });
      toast.success("Pergerakan stok tercatat.");
      onSaved();
    } catch (err) {
      toast.error((err as ApiError).message ?? "Gagal mencatat pergerakan.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Catat Pergerakan Stok"
      width="w-full sm:max-w-lg"
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} className="app-grid-btn" disabled={submitting}>
            Batal
          </button>
          <button
            type="submit"
            form="inv-form"
            disabled={submitting}
            className="app-grid-btn app-grid-btn-primary"
          >
            {submitting ? "Menyimpan…" : "Simpan"}
          </button>
        </div>
      }
    >
      <p className="mb-4 text-xs text-[var(--muted-foreground)]">
        Pilih produk dan masukkan quantity. Tanda quantity otomatis mengikuti jenis pergerakan.
      </p>
      <form id="inv-form" onSubmit={handleSubmit} className="space-y-3">
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-bold text-[var(--card-foreground)]">
            Produk <span className="text-[var(--danger)]">*</span>
          </label>
          <select
            name="productId"
            className="app-grid-filter-input"
            required
            defaultValue=""
            disabled={productsLoading}
          >
            <option value="" disabled>
              {productsLoading ? "Memuat…" : "Pilih produk…"}
            </option>
            {products.map((p) => (
              <option key={p.id} value={p.id}>{p.name} ({p.sku})</option>
            ))}
          </select>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-bold text-[var(--card-foreground)]">
              Jenis Pergerakan <span className="text-[var(--danger)]">*</span>
            </label>
            <select name="type" className="app-grid-filter-input" defaultValue="Inbound" required>
              {Object.entries(TYPE_LABEL).map(([k, v]) => (
                <option key={k} value={k}>{v}</option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-bold text-[var(--card-foreground)]">
              Quantity <span className="text-[var(--danger)]">*</span>
            </label>
            <input name="quantity" type="number" min={1} defaultValue={1} className="app-grid-filter-input" required />
          </div>
        </div>
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-bold text-[var(--card-foreground)]">Referensi</label>
          <input name="reference" className="app-grid-filter-input" placeholder="cth. nomor PO, nomor invoice, dll." />
        </div>
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-bold text-[var(--card-foreground)]">Catatan</label>
          <textarea name="note" rows={2} className="app-grid-filter-input" />
        </div>
      </form>
    </Modal>
  );
}
