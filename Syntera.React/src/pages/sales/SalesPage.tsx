import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Trash2, ShoppingCart, Receipt } from "lucide-react";
import { saleApi, customerApi } from "../../api/operations";
import { productApi } from "../../api/catalog";
import type {
  CustomerDto, ProductDto, SaleDto, SaleStatus,
} from "../../types";
import { formatIDR, formatNumber, formatDateTime } from "../../lib/format";
import { DataTable, type DataTableColumn } from "../../components/DataTable";
import { Modal, Field, inputClass, btnPrimary, btnGhost } from "../../components/Modal";
import { ApiError } from "../../api/client";

const STATUS_LABEL: Record<SaleStatus, string> = {
  Draft: "Draf",
  Pending: "Menunggu",
  Paid: "Lunas",
  Shipped: "Dikirim",
  Completed: "Selesai",
  Cancelled: "Dibatalkan",
};

const STATUS_COLOR: Record<SaleStatus, string> = {
  Draft: "bg-[var(--muted)] text-[var(--muted-foreground)]",
  Pending: "bg-[var(--warning)]/15 text-[var(--warning)]",
  Paid: "bg-[var(--success)]/15 text-[var(--success)]",
  Shipped: "bg-[var(--info)]/15 text-[var(--info)]",
  Completed: "bg-[var(--primary)]/15 text-[var(--primary)]",
  Cancelled: "bg-[var(--danger)]/15 text-[var(--danger)]",
};

interface CartLine {
  product: ProductDto;
  quantity: number;
  unitPrice: number;
  discount: number;
}

export default function SalesPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);

  const columns: DataTableColumn<SaleDto>[] = [
    {
      key: "invoice",
      header: "Invoice",
      render: (s) => (
        <div>
          <p className="font-mono font-semibold">{s.invoiceNumber}</p>
          <p className="text-xs text-[var(--muted-foreground)]">
            {formatDateTime(s.createdAt)}
          </p>
        </div>
      ),
    },
    {
      key: "customer",
      header: "Pelanggan",
      render: (s) => (
        <div>
          <p className="font-medium">{s.customerName}</p>
          {s.items.length > 0 && (
            <p className="text-xs text-[var(--muted-foreground)]">
              {s.items.length} item
            </p>
          )}
        </div>
      ),
    },
    {
      key: "saleDate",
      header: "Tgl. Jual",
      render: (s) => (
        <span className="text-xs">{s.saleDate ? formatDateTime(s.saleDate) : "—"}</span>
      ),
    },
    {
      key: "items",
      header: "Item",
      render: (s) => (
        <div className="text-xs">
          {s.items.slice(0, 3).map((i) => (
            <div key={i.id} className="truncate">
              {i.quantity}× {i.productName}
            </div>
          ))}
          {s.items.length > 3 && (
            <div className="text-[var(--muted-foreground)]">
              +{s.items.length - 3} lainnya
            </div>
          )}
        </div>
      ),
    },
    {
      key: "status",
      header: "Status",
      render: (s) => (
        <span className={`rounded-full px-2 py-1 text-xs ${STATUS_COLOR[s.status]}`}>
          {STATUS_LABEL[s.status]}
        </span>
      ),
    },
    {
      key: "total",
      header: "Total",
      render: (s) => (
        <div className="text-right">
          <span className="font-bold">{formatIDR(s.grandTotal)}</span>
          <p className="text-[10px] text-[var(--muted-foreground)]">
            Sub: {formatIDR(s.subTotal)} • PPN: {formatIDR(s.taxAmount)}
          </p>
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight">Penjualan</h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Riwayat invoice POS — checkout pelanggan B2B / B2C.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setCreateOpen(true)}
          className={`${btnPrimary} flex items-center gap-2`}
        >
          <Plus size={16} /> Penjualan Baru
        </button>
      </header>

      <DataTable<SaleDto>
        columns={columns}
        rowKey={(s) => s.id}
        load={async ({ page, pageSize, search }) => {
          const res = await saleApi.page({ page, pageSize, search });
          return { items: res.items, total: res.total, totalPages: res.totalPages };
        }}
      />

      {createOpen && (
        <NewSaleModal
          open
          onClose={() => setCreateOpen(false)}
          onSaved={() => {
            void queryClient.invalidateQueries({ queryKey: ["sales"] });
            void queryClient.invalidateQueries({ queryKey: ["dashboard-summary"] });
            void queryClient.invalidateQueries({ queryKey: ["dashboard-trend"] });
            void queryClient.invalidateQueries({ queryKey: ["inventory"] });
            void queryClient.invalidateQueries({ queryKey: ["products"] });
            setCreateOpen(false);
          }}
        />
      )}
    </div>
  );
}

function NewSaleModal({
  open,
  onClose,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [customers, setCustomers] = useState<CustomerDto[]>([]);
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [customerId, setCustomerId] = useState("");
  const [taxRate, setTaxRate] = useState(11);
  const [discount, setDiscount] = useState(0);
  const [note, setNote] = useState("");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [addingProductId, setAddingProductId] = useState("");
  const [addingQty, setAddingQty] = useState(1);
  const [submitting, setSubmitting] = useState(false);

  // Lazy-load once
  if (customers.length === 0) {
    customerApi.page({ pageSize: 200 }).then((r) => setCustomers(r.items)).catch(() => undefined);
  }
  if (products.length === 0) {
    productApi.search({ pageSize: 200, activeOnly: true }).then((r) => setProducts(r.items)).catch(() => undefined);
  }

  const subTotal = cart.reduce((acc, l) => acc + (l.unitPrice * l.quantity) - l.discount, 0);
  const taxAmount = Math.round(subTotal * taxRate / 100);
  const grandTotal = subTotal + taxAmount - discount;

  const addToCart = () => {
    const p = products.find((x) => x.id === addingProductId);
    if (!p || addingQty < 1) return;
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
      open={open}
      onClose={onClose}
      title="Buat Penjualan Baru"
      description="Pilih pelanggan, tambahkan produk, lalu simpan untuk generate invoice."
      size="xl"
      footer={
        <>
          <button type="button" className={btnGhost} onClick={onClose}>Batal</button>
          <button type="button" onClick={handleSubmit} disabled={submitting} className={btnPrimary}>
            {submitting ? "Menyimpan…" : "Simpan Invoice"}
          </button>
        </>
      }
    >
      <div className="space-y-4">
        {/* Customer + tax + discount */}
        <div className="grid grid-cols-3 gap-3">
          <Field label="Pelanggan" required>
            <select
              value={customerId}
              onChange={(e) => setCustomerId(e.target.value)}
              className={inputClass}
              required
            >
              <option value="">Pilih…</option>
              {customers.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </Field>
          <Field label="PPN (%)">
            <input
              type="number"
              min={0}
              max={100}
              value={taxRate}
              onChange={(e) => setTaxRate(Number(e.target.value))}
              className={inputClass}
            />
          </Field>
          <Field label="Diskon (Rp)">
            <input
              type="number"
              min={0}
              value={discount}
              onChange={(e) => setDiscount(Number(e.target.value))}
              className={inputClass}
            />
          </Field>
        </div>

        {/* Product picker */}
        <div className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
          <div className="grid grid-cols-[1fr_auto_auto] items-end gap-2">
            <Field label="Tambah Produk">
              <select
                value={addingProductId}
                onChange={(e) => setAddingProductId(e.target.value)}
                className={inputClass}
              >
                <option value="">Pilih produk…</option>
                {products.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.sku}) — {formatIDR(p.sellingPrice)} • stok {p.stock}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Qty">
              <input
                type="number"
                min={1}
                value={addingQty}
                onChange={(e) => setAddingQty(Number(e.target.value))}
                className={`${inputClass} w-24`}
              />
            </Field>
            <button
              type="button"
              onClick={addToCart}
              disabled={!addingProductId}
              className={`${btnPrimary} flex items-center gap-1`}
            >
              <Plus size={16} /> Tambah
            </button>
          </div>
        </div>

        {/* Cart */}
        <div className="overflow-hidden rounded-xl border border-[var(--border)]">
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
                    <p className="font-medium">{l.product.name}</p>
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

        <Field label="Catatan">
          <textarea
            rows={2}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            className={inputClass}
            placeholder="cth. Pengiriman ke alamat…"
          />
        </Field>
      </div>
    </Modal>
  );
}
