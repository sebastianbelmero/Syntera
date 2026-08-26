import { useEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Trash2, ShoppingCart, Receipt, ArrowUpDown } from "lucide-react";
import { saleApi, customerApi } from "../../api/operations";
import { productApi } from "../../api/catalog";
import type {
  CustomerDto, ProductDto, SaleItemDto, SaleStatus,
} from "../../types";
import { formatIDR, formatNumber, formatDateTime } from "../../lib/format";
import {
  AppGrid,
  type ColumnProps,
} from "../../kalventis/ui";
import { Modal } from "../../kalventis/ui";
import { Badge } from "../../kalventis/ui";
import { ApiError } from "../../api/client";

const STATUS_LABEL: Record<SaleStatus, string> = {
  Draft: "Draf",
  Pending: "Menunggu",
  Paid: "Lunas",
  Shipped: "Dikirim",
  Completed: "Selesai",
  Cancelled: "Dibatalkan",
};

const STATUS_VARIANT: Record<SaleStatus, "neutral" | "warning" | "success" | "info" | "default" | "destructive"> = {
  Draft: "neutral",
  Pending: "warning",
  Paid: "success",
  Shipped: "info",
  Completed: "default",
  Cancelled: "destructive",
};

interface CartLine {
  product: ProductDto;
  quantity: number;
  unitPrice: number;
  discount: number;
}

/**
 * SalesPage — migrated to kalventis AppGrid pattern for display +
 * master-detail (sale items shown as a sub-table inside expanded rows).
 *
 * NewSaleModal stays as a custom Modal (not AppDynamicForm) because:
 *   - It's a cart-based multi-line entry (not a single-record upsert).
 *   - Items[] is an array field with custom row-level add/remove/qty edit
 *     that doesn't fit AppDynamicForm's array editor.
 *   - Submit goes to /api/sales with a complex payload (cart + taxRate +
 *     discount + customer + note) — different from a flat entity upsert.
 *
 * The Modal itself uses the kalventis Modal (focus-trapped + animated
 * + responsive width). Form fields use the .app-grid-filter-input /
 * .app-grid-btn CSS classes that kalventis styles.css exports.
 */
export default function SalesPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);

  // Fetch customers + active products for the NewSaleModal. Hoisted
  // to page level so the modal opens instantly with options populated
  // (was lazy-loaded in the modal's useEffect in the previous version).
  const customersQ = useQuery({
    queryKey: ["customers-list"],
    queryFn: () => customerApi.page({ pageSize: 200 }),
  });
  const productsQ = useQuery({
    queryKey: ["products-active-list"],
    queryFn: () => productApi.search({ pageSize: 200, activeOnly: true }),
  });

  const columns: ColumnProps[] = useMemo(() => [
    {
      dataField: "invoiceNumber",
      caption: "Invoice",
      allowEditing: false,
      visibleInForm: false,
      cellRender: ({ row }: any) => (
        <div>
          <p className="font-mono font-semibold text-[var(--card-foreground)]">{row.invoiceNumber}</p>
          <p className="text-xs text-[var(--muted-foreground)]">
            {formatDateTime(row.createdAt)}
          </p>
        </div>
      ),
    },
    {
      dataField: "customerName",
      caption: "Pelanggan",
      allowEditing: false,
      visibleInForm: false,
      cellRender: ({ row }: any) => (
        <div>
          <p className="font-medium text-[var(--card-foreground)]">{row.customerName}</p>
          <p className="text-xs text-[var(--muted-foreground)]">
            {row.items?.length ?? 0} item
          </p>
        </div>
      ),
    },
    {
      dataField: "saleDate",
      caption: "Tgl. Jual",
      dataType: "date",
      allowEditing: false,
      visibleInForm: false,
      cellRender: ({ row }: any) => (
        <span className="text-xs">
          {row.saleDate ? formatDateTime(row.saleDate) : "—"}
        </span>
      ),
    },
    {
      dataField: "status",
      caption: "Status",
      hideOnMobile: true,
      allowEditing: false,
      visibleInForm: false,
      cellRender: ({ row }: any) => (
        <Badge variant={STATUS_VARIANT[row.status as SaleStatus]}>
          {STATUS_LABEL[row.status as SaleStatus]}
        </Badge>
      ),
    },
    {
      dataField: "grandTotal",
      caption: "Total",
      dataType: "number",
      alignment: "right",
      allowEditing: false,
      visibleInForm: false,
      cellRender: ({ row }: any) => (
        <div className="text-right">
          <span className="font-bold">{formatIDR(row.grandTotal)}</span>
          <p className="text-[10px] text-[var(--muted-foreground)]">
            Sub: {formatIDR(row.subTotal)} • PPN: {formatIDR(row.taxAmount)}
          </p>
        </div>
      ),
    },
  ], []);

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight text-[var(--card-foreground)]">
            Penjualan
          </h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Riwayat invoice POS — klik baris untuk membuka rincian item.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setCreateOpen(true)}
          className="app-grid-btn app-grid-btn-primary"
        >
          <Plus size={14} className="mr-1.5" /> Penjualan Baru
        </button>
      </header>

      <AppGrid
        apiEndpoint="/api/sales/grid"
        enableFiltering
        enableSorting
        enableColumnChooser
        enablePagination
        enableStatePersistence
        persistenceKey="sales-grid"
        globalFilterFields={["invoiceNumber", "customerName"]}
        title="Daftar Penjualan"
        pageSize={10}
        enableMasterDetail
        renderMasterDetail={(row: any) => <SaleItemsSubTable items={row.items ?? []} />}
        dynamicColumns={columns}
      />

      {createOpen && (
        <NewSaleModal
          isOpen
          customers={customersQ.data?.items ?? []}
          products={productsQ.data?.items ?? []}
          optsLoading={customersQ.isLoading || productsQ.isLoading}
          onClose={() => setCreateOpen(false)}
          onSaved={() => {
            void queryClient.invalidateQueries({ queryKey: ["sales"] });
            void queryClient.invalidateQueries({ queryKey: ["dashboard-summary"] });
            void queryClient.invalidateQueries({ queryKey: ["dashboard-trend"] });
            void queryClient.invalidateQueries({ queryKey: ["inventory"] });
            void queryClient.invalidateQueries({ queryKey: ["products"] });
            void queryClient.invalidateQueries({ queryKey: ["products-active-list"] });
            setCreateOpen(false);
          }}
        />
      )}
    </div>
  );
}

/**
 * SaleItemsSubTable — renders the sale's items[] as a compact read-only
 * sub-table inside the expanded row of the parent AppGrid. This is the
 * "table in table" master-detail pattern.
 *
 * Uses simple inline <table> + <tr> markup (not a nested AppGrid) because:
 *   - The data is read-only (no CRUD on sub-items).
 *   - Items are eagerly loaded with the sale, no server round-trip.
 *   - Avoids the AppGrid's toolbar/pager/filter chrome which would be
 *     visual noise for a 1-10 row detail.
 */
function SaleItemsSubTable({ items }: { items: SaleItemDto[] }) {
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2 text-xs text-[var(--muted-foreground)]">
        <ArrowUpDown size={12} />
        <span>{items.length} item dalam invoice ini</span>
      </div>
      <div className="overflow-hidden rounded-lg border border-[var(--border)] bg-[var(--card)]">
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] bg-[var(--surface)]">
                <th className="px-3 py-2 text-left text-xs font-semibold text-[var(--muted-foreground)]">Produk</th>
                <th className="px-3 py-2 text-right text-xs font-semibold text-[var(--muted-foreground)]">Qty</th>
                <th className="px-3 py-2 text-right text-xs font-semibold text-[var(--muted-foreground)]">Harga</th>
                <th className="px-3 py-2 text-right text-xs font-semibold text-[var(--muted-foreground)]">Diskon</th>
                <th className="px-3 py-2 text-right text-xs font-semibold text-[var(--muted-foreground)]">Subtotal</th>
              </tr>
            </thead>
            <tbody>
              {items.map((i) => (
                <tr key={i.id} className="border-b border-[var(--border)] last:border-0">
                  <td className="px-3 py-2">
                    <p className="font-medium text-[var(--card-foreground)]">{i.productName}</p>
                    <p className="text-[10px] text-[var(--muted-foreground)]">{i.productSku}</p>
                  </td>
                  <td className="px-3 py-2 text-right font-mono">{formatNumber(i.quantity)}</td>
                  <td className="px-3 py-2 text-right">{formatIDR(i.unitPrice)}</td>
                  <td className="px-3 py-2 text-right text-[var(--muted-foreground)]">
                    {i.discountAmount > 0 ? `−${formatIDR(i.discountAmount)}` : "—"}
                  </td>
                  <td className="px-3 py-2 text-right font-semibold">{formatIDR(i.lineTotal)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

// ── NewSaleModal — cart-based sale creation ─────────────────
// Custom Modal (not AppDynamicForm) because cart + tax + discount +
// multi-item is too complex for a flat schema.
function NewSaleModal({
  isOpen,
  customers,
  products,
  optsLoading,
  onClose,
  onSaved,
}: {
  isOpen: boolean;
  customers: CustomerDto[];
  products: ProductDto[];
  optsLoading: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [customerId, setCustomerId] = useState("");
  const [taxRate, setTaxRate] = useState(11);
  const [discount, setDiscount] = useState(0);
  const [note, setNote] = useState("");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [addingProductId, setAddingProductId] = useState("");
  const [addingQty, setAddingQty] = useState(1);
  const [submitting, setSubmitting] = useState(false);

  // Reset cart when modal re-opens. Without this, opening a fresh
  // NewSaleModal after a previous sale would show stale cart lines.
  useEffect(() => {
    if (isOpen) {
      setCustomerId("");
      setTaxRate(11);
      setDiscount(0);
      setNote("");
      setCart([]);
      setAddingProductId("");
      setAddingQty(1);
    }
  }, [isOpen]);

  const subTotal = cart.reduce((acc, l) => acc + (l.unitPrice * l.quantity) - l.discount, 0);
  const taxAmount = Math.round(subTotal * taxRate / 100);
  const grandTotal = subTotal + taxAmount - discount;

  const addToCart = () => {
    const p = products.find((x) => x.id === addingProductId);
    if (!p || addingQty < 1) return;
    // Prevent overselling: cap cumulative cart quantity at product
    // stock for the line being added / extended.
    const existingQty = cart.find((l) => l.product.id === p.id)?.quantity ?? 0;
    if (existingQty + addingQty > p.stock) {
      toast.error(`Stok tidak cukup. Tersedia ${p.stock}, diminta ${existingQty + addingQty}.`);
      return;
    }
    setCart((prev) => {
      const existing = prev.find((l) => l.product.id === p.id);
      if (existing) {
        return prev.map((l) =>
          l.product.id === p.id ? { ...l, quantity: l.quantity + addingQty } : l
        );
      }
      return [...prev, { product: p, quantity: addingQty, unitPrice: p.sellingPrice, discount: 0 }];
    });
    setAddingProductId("");
    setAddingQty(1);
  };

  const removeLine = (productId: string) => {
    setCart((prev) => prev.filter((l) => l.product.id !== productId));
  };

  const handleSubmit = async () => {
    if (!customerId) {
      toast.error("Pilih pelanggan terlebih dahulu.");
      return;
    }
    if (cart.length === 0) {
      toast.error("Keranjang masih kosong.");
      return;
    }
    setSubmitting(true);
    try {
      const created = await saleApi.create({
        customerId,
        saleDate: new Date().toISOString(),
        taxRate,
        discountAmount: discount,
        note: note || null,
        items: cart.map((l) => ({
          productId: l.product.id,
          quantity: l.quantity,
          unitPrice: l.unitPrice,
          discountAmount: l.discount,
        })),
      });
      toast.success(`Invoice ${created.invoiceNumber} dibuat (${formatIDR(created.grandTotal)}).`);
      onSaved();
    } catch (err) {
      toast.error((err as ApiError).message ?? "Gagal membuat penjualan.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Buat Penjualan Baru"
      width="w-full sm:max-w-3xl"
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} className="app-grid-btn" disabled={submitting}>
            Batal
          </button>
          <button
            type="button"
            onClick={handleSubmit}
            disabled={submitting}
            className="app-grid-btn app-grid-btn-primary"
          >
            {submitting ? "Menyimpan…" : "Simpan Invoice"}
          </button>
        </div>
      }
    >
      <p className="mb-4 text-xs text-[var(--muted-foreground)]">
        Pilih pelanggan, tambahkan produk, lalu simpan untuk generate invoice.
      </p>

      <div className="space-y-4">
        {/* Customer + tax + discount */}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-bold text-[var(--card-foreground)]">
              Pelanggan <span className="text-[var(--danger)]">*</span>
            </label>
            <select
              value={customerId}
              onChange={(e) => setCustomerId(e.target.value)}
              className="app-grid-filter-input"
              required
              disabled={optsLoading}
            >
              <option value="">{optsLoading ? "Memuat…" : "Pilih…"}</option>
              {customers.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-bold text-[var(--card-foreground)]">PPN (%)</label>
            <input
              type="number"
              min={0}
              max={100}
              step="0.01"
              value={taxRate}
              onChange={(e) => setTaxRate(Number(e.target.value))}
              className="app-grid-filter-input"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-bold text-[var(--card-foreground)]">Diskon (Rp)</label>
            <input
              type="number"
              min={0}
              value={discount}
              onChange={(e) => setDiscount(Number(e.target.value))}
              className="app-grid-filter-input"
            />
          </div>
        </div>

        {/* Product picker */}
        <div className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
          <div className="grid grid-cols-1 items-end gap-2 sm:grid-cols-[1fr_auto_auto]">
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-bold text-[var(--card-foreground)]">Tambah Produk</label>
              <select
                value={addingProductId}
                onChange={(e) => setAddingProductId(e.target.value)}
                className="app-grid-filter-input"
                disabled={optsLoading}
              >
                <option value="">{optsLoading ? "Memuat…" : "Pilih produk…"}</option>
                {products.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.sku}) — {formatIDR(p.sellingPrice)} • stok {p.stock}
                  </option>
                ))}
              </select>
            </div>
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-bold text-[var(--card-foreground)]">Qty</label>
              <input
                type="number"
                min={1}
                value={addingQty}
                onChange={(e) => setAddingQty(Number(e.target.value))}
                className="app-grid-filter-input w-full sm:w-24"
              />
            </div>
            <button
              type="button"
              onClick={addToCart}
              disabled={!addingProductId}
              className="app-grid-btn app-grid-btn-primary flex items-center justify-center gap-1"
            >
              <Plus size={14} /> Tambah
            </button>
          </div>
        </div>

        {/* Cart */}
        <div className="overflow-hidden rounded-xl border border-[var(--border)]">
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-sm">
              <thead className="bg-[var(--surface)]">
                <tr>
                  <th className="px-3 py-2 text-left font-medium">Produk</th>
                  <th className="px-3 py-2 text-right font-medium">Qty</th>
                  <th className="px-3 py-2 text-right font-medium">Harga</th>
                  <th className="px-3 py-2 text-right font-medium">Subtotal</th>
                  <th className="px-3 py-2"></th>
                </tr>
              </thead>
              <tbody>
                {cart.length === 0 && (
                  <tr>
                    <td colSpan={5} className="px-3 py-6 text-center text-[var(--muted-foreground)]">
                      <ShoppingCart className="mx-auto mb-2 opacity-50" size={20} />
                      Keranjang kosong
                    </td>
                  </tr>
                )}
                {cart.map((l) => (
                  <tr key={l.product.id} className="border-t border-[var(--border)]">
                    <td className="px-3 py-2">
                      <p className="font-medium text-[var(--card-foreground)]">{l.product.name}</p>
                      <p className="text-xs text-[var(--muted-foreground)]">
                        {l.product.sku} • stok {l.product.stock}
                      </p>
                    </td>
                    <td className="px-3 py-2 text-right">{formatNumber(l.quantity)}</td>
                    <td className="px-3 py-2 text-right">{formatIDR(l.unitPrice)}</td>
                    <td className="px-3 py-2 text-right font-semibold">
                      {formatIDR(l.unitPrice * l.quantity - l.discount)}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <button
                        type="button"
                        onClick={() => removeLine(l.product.id)}
                        aria-label={`Hapus ${l.product.name} dari keranjang`}
                        title="Hapus dari keranjang"
                        className="rounded-md p-1 text-[var(--muted-foreground)] hover:text-[var(--danger)]"
                      >
                        <Trash2 size={14} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
              {cart.length > 0 && (
                <tfoot className="bg-[var(--surface)]">
                  <tr className="border-t border-[var(--border)]">
                    <td colSpan={3} className="px-3 py-2 text-right text-sm">Subtotal</td>
                    <td className="px-3 py-2 text-right font-semibold">{formatIDR(subTotal)}</td>
                    <td></td>
                  </tr>
                  <tr>
                    <td colSpan={3} className="px-3 py-2 text-right text-sm">PPN {taxRate}%</td>
                    <td className="px-3 py-2 text-right">{formatIDR(taxAmount)}</td>
                    <td></td>
                  </tr>
                  <tr>
                    <td colSpan={3} className="px-3 py-2 text-right text-sm">Diskon</td>
                    <td className="px-3 py-2 text-right">−{formatIDR(discount)}</td>
                    <td></td>
                  </tr>
                  <tr className="border-t border-[var(--border)]">
                    <td colSpan={3} className="px-3 py-2 text-right font-bold">
                      <Receipt size={14} className="mr-1 inline" />
                      Grand Total
                    </td>
                    <td className="px-3 py-2 text-right text-base font-bold text-[var(--primary)]">
                      {formatIDR(grandTotal)}
                    </td>
                    <td></td>
                  </tr>
                </tfoot>
              )}
            </table>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-bold text-[var(--card-foreground)]">Catatan</label>
          <textarea
            rows={2}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            className="app-grid-filter-input"
            placeholder="cth. Pengiriman ke alamat…"
          />
        </div>
      </div>
    </Modal>
  );
}
