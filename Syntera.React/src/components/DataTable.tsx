import { useState } from "react";
import type { ReactNode } from "react";
import { ChevronLeft, ChevronRight, Search } from "lucide-react";
import { cn } from "../lib/cn";

/**
 * DataTable — generic client-side paginated table that calls a
 * `load` function returning PagedResult<T>. All pages share the
 * same skeleton: search box + thead + tbody + footer pagination.
 * The cell renderer is a single function per row, keeping the
 * column layout DRY across catalog/parties/inventory pages.
 */
export interface DataTableColumn<T> {
  key: string;
  header: string;
  render: (row: T) => ReactNode;
  className?: string;
}

export interface DataTableProps<T> {
  columns: DataTableColumn<T>[];
  load: (params: { page: number; pageSize: number; search: string }) =>
    Promise<{ items: T[]; total: number; totalPages: number }>;
  rowKey: (row: T) => string;
  emptyMessage?: string;
  toolbar?: ReactNode;
  onRowClick?: (row: T) => void;
  initialPageSize?: number;
}

export function DataTable<T>({
  columns,
  load,
  rowKey,
  emptyMessage = "Tidak ada data.",
  toolbar,
  onRowClick,
  initialPageSize = 10,
}: DataTableProps<T>) {
  const [page, setPage] = useState(1);
  const [pageSize] = useState(initialPageSize);
  const [search, setSearch] = useState("");
  const [data, setData] = useState<T[]>([]);
  const [total, setTotal] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reload = async (p: number = page, s: string = search) => {
    setLoading(true);
    setError(null);
    try {
      const res = await load({ page: p, pageSize, search: s });
      setData(res.items);
      setTotal(res.total);
      setTotalPages(res.totalPages);
    } catch (err) {
      setError((err as Error).message ?? "Gagal memuat data.");
      setData([]);
      setTotal(0);
      setTotalPages(0);
    } finally {
      setLoading(false);
    }
  };

  // Lazy-load on first render
  if (data.length === 0 && !loading && !error && page === 1 && search === "") {
    Promise.resolve().then(() => reload(1, ""));
  }

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    reload(1, search);
  };

  return (
    <div className="flex flex-col gap-3">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-3">
        <form onSubmit={handleSearch} className="relative">
          <Search
            size={16}
            className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-[var(--muted-foreground)]"
          />
          <input
            type="search"
            placeholder="Cari…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full rounded-lg border border-[var(--input)] bg-white py-2 pl-9 pr-3 text-sm outline-none transition focus:border-[var(--primary)] focus:ring-2 focus:ring-[var(--ring)] sm:w-72"
          />
        </form>
        <div className="ml-auto flex items-center gap-2">{toolbar}</div>
      </div>

      {/* Table */}
      <div className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--card)]">
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] bg-[var(--surface)]">
                {columns.map((c) => (
                  <th
                    key={c.key}
                    className={cn(
                      "px-4 py-3 text-left font-semibold text-[var(--muted-foreground)]",
                      c.className,
                    )}
                  >
                    {c.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {loading && (
                <tr>
                  <td
                    colSpan={columns.length}
                    className="px-4 py-8 text-center text-[var(--muted-foreground)]"
                  >
                    Memuat…
                  </td>
                </tr>
              )}
              {!loading && error && (
                <tr>
                  <td
                    colSpan={columns.length}
                    className="px-4 py-8 text-center text-[var(--danger)]"
                  >
                    {error}
                  </td>
                </tr>
              )}
              {!loading && !error && data.length === 0 && (
                <tr>
                  <td
                    colSpan={columns.length}
                    className="px-4 py-8 text-center text-[var(--muted-foreground)]"
                  >
                    {emptyMessage}
                  </td>
                </tr>
              )}
              {!loading && !error && data.length > 0 && (
                data.map((row) => (
                  <tr
                    key={rowKey(row)}
                    onClick={onRowClick ? () => onRowClick(row) : undefined}
                    className={cn(
                      "border-b border-[var(--border)] last:border-0 transition-colors hover:bg-[var(--surface)]",
                      onRowClick && "cursor-pointer",
                    )}
                  >
                    {columns.map((c) => (
                      <td
                        key={c.key}
                        className={cn("px-4 py-3 align-top", c.className)}
                      >
                        {c.render(row)}
                      </td>
                    ))}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        <div className="flex items-center justify-between border-t border-[var(--border)] px-4 py-3 text-xs text-[var(--muted-foreground)]">
          <span>
            {total > 0 ? (
              <>Menampilkan {data.length} dari {total} data</>
            ) : (
              "—"
            )}
          </span>
          <div className="flex items-center gap-1">
            <button
              type="button"
              disabled={page <= 1 || loading}
              onClick={() => {
                const p = Math.max(1, page - 1);
                setPage(p);
                reload(p, search);
              }}
              className="grid h-7 w-7 place-items-center rounded-md border border-[var(--border)] transition hover:bg-[var(--surface)] disabled:cursor-not-allowed disabled:opacity-40"
              aria-label="Halaman sebelumnya"
            >
              <ChevronLeft size={14} />
            </button>
            <span className="px-2">
              {page} / {Math.max(1, totalPages)}
            </span>
            <button
              type="button"
              disabled={page >= totalPages || loading}
              onClick={() => {
                const p = Math.min(totalPages, page + 1);
                setPage(p);
                reload(p, search);
              }}
              className="grid h-7 w-7 place-items-center rounded-md border border-[var(--border)] transition hover:bg-[var(--surface)] disabled:cursor-not-allowed disabled:opacity-40"
              aria-label="Halaman berikutnya"
            >
              <ChevronRight size={14} />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
