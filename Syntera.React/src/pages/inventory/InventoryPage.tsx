import { Plus } from "lucide-react";
import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { inventoryApi } from "../../api/operations";
import { productApi } from "../../api/catalog";
import type { InventoryMovementDto, InventoryMovementType } from "../../types";
import { formatDateTime } from "../../lib/format";
import { DataTable, type DataTableColumn } from "../../components/DataTable";
import { Modal, Field, inputClass, btnPrimary, btnGhost } from "../../components/Modal";
import { ApiError } from "../../api/client";

const TYPE_LABEL: Record<InventoryMovementType, string> = {
  Inbound: "Masuk",
  Outbound: "Keluar",
  Adjustment: "Penyesuaian",
  Return: "Retur",
  Damage: "Kerusakan",
};

const TYPE_COLOR: Record<InventoryMovementType, string> = {
  Inbound: "bg-[var(--success)]/15 text-[var(--success)]",
  Outbound: "bg-[var(--info)]/15 text-[var(--info)]",
  Adjustment: "bg-[var(--warning)]/15 text-[var(--warning)]",
  Return: "bg-[var(--accent)]/30 text-[var(--accent-foreground)]",
  Damage: "bg-[var(--danger)]/15 text-[var(--danger)]",
};

export default function InventoryPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);

  const columns: DataTableColumn<InventoryMovementDto>[] = [
    {
      key: "createdAt",
      header: "Waktu",
      render: (m) => <span className="text-xs">{formatDateTime(m.createdAt)}</span>,
    },
    {
      key: "product",
      header: "Produk",
      render: (m) => (
        <div>
          <p className="font-medium">{m.productName}</p>
          <p className="text-xs text-[var(--muted-foreground)]">{m.productSku}</p>
        </div>
      ),
    },
    {
      key: "type",
      header: "Jenis",
      render: (m) => (
        <span className={`rounded-full px-2 py-1 text-xs ${TYPE_COLOR[m.type]}`}>
          {TYPE_LABEL[m.type]}
        </span>
      ),
    },
    {
      key: "qty",
      header: "Quantity",
      render: (m) => (
        <span className={`font-semibold ${m.quantity > 0 ? "text-[var(--success)]" : "text-[var(--danger)]"}`}>
          {m.quantity > 0 ? "+" : ""}{m.quantity}
        </span>
      ),
    },
    {
      key: "balance",
      header: "Saldo",
      render: (m) => <span className="font-medium">{m.balanceAfter}</span>,
    },
    {
      key: "ref",
      header: "Referensi",
      render: (m) => (
        <span className="text-xs text-[var(--muted-foreground)]">
          {m.reference ?? "—"}
        </span>
      ),
    },
    {
      key: "note",
      header: "Catatan",
      render: (m) => (
        <span className="text-xs text-[var(--muted-foreground)]">{m.note ?? "—"}</span>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight">Persediaan</h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Ledger pergerakan stok — single source of truth untuk
            on-hand quantity.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setCreateOpen(true)}
          className={`${btnPrimary} flex items-center gap-2`}
        >
          <Plus size={16} /> Catat Pergerakan
        </button>
      </header>

      <DataTable<InventoryMovementDto>
        columns={columns}
        rowKey={(m) => m.id}
        load={async ({ page, pageSize, search }) => {
          const res = await inventoryApi.page({ page, pageSize, search });
          return { items: res.items, total: res.total, totalPages: res.totalPages };
        }}
      />

      {createOpen && (
        <InventoryFormModal
          open
          onClose={() => setCreateOpen(false)}
          onSaved={() => {
            void queryClient.invalidateQueries({ queryKey: ["inventory"] });
            void queryClient.invalidateQueries({ queryKey: ["products"] });
            setCreateOpen(false);
          }}
        />
      )}
    </div>
  );
}

function InventoryFormModal({
  open,
  onClose,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [submitting, setSubmitting] = useState(false);
  const [products, setProducts] = useState<{ id: string; name: string; sku: string }[]>([]);

  // Lazy load product list when modal first opens
  if (products.length === 0) {
    productApi.search({ pageSize: 200 }).then((res) =>
      setProducts(res.items.map((p) => ({ id: p.id, name: p.name, sku: p.sku })))
    ).catch(() => undefined);
  }

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
      open={open}
      onClose={onClose}
      title="Catat Pergerakan Stok"
      description="Pilih produk dan masukkan quantity. Tanda quantity otomatis mengikuti jenis pergerakan."
      size="lg"
      footer={
        <>
          <button type="button" className={btnGhost} onClick={onClose}>Batal</button>
          <button type="submit" form="inv-form" disabled={submitting} className={btnPrimary}>
            {submitting ? "Menyimpan…" : "Simpan"}
          </button>
        </>
      }
    >
      <form id="inv-form" onSubmit={handleSubmit} className="space-y-3">
        <Field label="Produk" required>
          <select name="productId" className={inputClass} required defaultValue="">
            <option value="" disabled>Pilih produk…</option>
            {products.map((p) => (
              <option key={p.id} value={p.id}>{p.name} ({p.sku})</option>
            ))}
          </select>
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Jenis Pergerakan" required>
            <select name="type" className={inputClass} defaultValue="Inbound" required>
              {Object.entries(TYPE_LABEL).map(([k, v]) => (
                <option key={k} value={k}>{v}</option>
              ))}
            </select>
          </Field>
          <Field label="Quantity" required>
            <input name="quantity" type="number" min={1} defaultValue={1} className={inputClass} required />
          </Field>
        </div>
        <Field label="Referensi" hint="cth. nomor PO, nomor invoice, dll.">
          <input name="reference" className={inputClass} />
        </Field>
        <Field label="Catatan">
          <textarea name="note" rows={2} className={inputClass} />
        </Field>
      </form>
    </Modal>
  );
}
