import { useEffect, useMemo, useRef, useState } from "react";
import type { KeyboardEvent, ReactNode } from "react";
import {
  ArrowDown,
  ArrowUp,
  ChevronDown,
  ChevronRight,
  ChevronLeft,
  ChevronsUpDown,
  Search,
} from "lucide-react";
import { cn } from "../lib/cn";

/**
 * AppGrid — powerful, recursive data grid.
 *
 * Designed as a drop-in replacement for DataTable with three
 * capabilities the previous grid couldn't do:
 *
 * 1. Master-detail expansion — each row can expand to reveal an
 *    arbitrary React node (a sub-table, a child AppGrid, a detail
 *    panel, an inline form, etc.). The expansion state is
 *    component-local so the parent can re-render freely without
 *    collapsing open rows.
 *
 * 2. AppGrid-in-AppGrid — because renderDetail returns a ReactNode,
 *    it can itself be another <AppGrid>. This is the "table in
 *    table" pattern: e.g. Sales row expands to an inner AppGrid
 *    listing SaleItem rows; that inner grid can itself expand if
 *    needed (e.g. show per-item discount breakdown).
 *
 * 3. Optional column sorting — opt in per column via `sortable:
 *    true` + `sortAccessor(row)`. The grid maintains an
 *    `{ column, direction }` state and re-requests from `load`
 *    with a `sort` param so the backend can paginate sorted
 *    results. For client-only data, sorting happens in-page.
 *
 * Plus ergonomic improvements over the old grid:
 *   - Skeleton loading (animated placeholder rows) instead of "Memuat…"
 *   - Mobile-responsive: collapses to stacked card layout on
 *     screens < md breakpoint, so wide tables don't horizontally
 *     scroll on phones.
 *   - Sticky header so column titles stay visible while scrolling
 *     a long page body.
 *   - Sort indicator icon (asc / desc / unsorted) on every sortable
 *     header.
 *
 * Backward compatibility: every DataTable prop is still accepted
 * with the same shape, so existing pages can swap <DataTable> for
 * <AppGrid> without touching their JSX.
 */

export type SortDirection = "asc" | "desc" | null;

export interface AppGridColumn<T> {
  /** Stable React key for the column. */
  key: string;
  /** Header label. */
  header: string;
  /** Cell renderer. */
  render: (row: T) => ReactNode;
  /** Optional className applied to BOTH <th> and <td>. */
  className?: string;
  /** Right-align numeric columns, etc. */
  align?: "left" | "right" | "center";
  /**
   * Opt into column sorting. When true, the header becomes a
   * button that cycles sort direction: asc → desc → null.
   */
  sortable?: boolean;
  /**
   * Sort accessor — given a row, return the primitive value
   * to sort by. Required when `sortable: true` and the grid
   * is in client-sort mode (no `sort` param forwarded to
   * `load`). If omitted, defaults to the row itself.
   */
  sortAccessor?: (row: T) => string | number | boolean | null | undefined;
  /**
   * Hide this column on small screens (mobile card view). Use
   * for secondary info that the card view can drop or merge
   * into the primary cell.
   */
  hideOnMobile?: boolean;
}

export interface AppGridProps<T> {
  columns: AppGridColumn<T>[];
  /**
   * Async loader. The grid calls this on initial mount, on
   * search submit, and on page navigation. If `sort` support
   * is enabled, the loader should accept a `sort` param and
   * forward it to the backend; otherwise the grid falls back
   * to in-memory sort.
   */
  load: (params: {
    page: number;
    pageSize: number;
    search: string;
    sort?: { column: string; direction: "asc" | "desc" } | null;
  }) => Promise<{ items: T[]; total: number; totalPages: number }>;
  rowKey: (row: T) => string;
  emptyMessage?: string;
  toolbar?: ReactNode;
  onRowClick?: (row: T) => void;
  initialPageSize?: number;
  /**
   * Master-detail expansion config. When provided, each row
   * gets an expand toggle (chevron). Clicking the chevron
   * reveals `renderDetail(row)` inline below the row.
   *
   * The detail ReactNode can be anything: a sub-AppGrid, a
   * custom panel, an inline form, etc. — enabling the
   * recursive "AppGrid-in-AppGrid" pattern.
   */
  expandable?: {
    /**
     * Render the detail panel for an expanded row. Receives
     * the row + a callback to collapse (useful for
     * "done"-style action buttons inside the detail).
     */
    renderDetail: (row: T, collapse: () => void) => ReactNode;
    /**
     * Which rows start expanded on first load. Defaults to
     * none. Useful for "expand all" toolbars.
     */
    initiallyExpanded?: (row: T) => boolean;
    /**
     * Lazy-load the detail body only on first expand. When
     * true, renderDetail is not invoked until the user
     * opens the row, and is unmounted when collapsed
     * (cancels inflight fetches / clears memory).
     */
    lazy?: boolean;
    /**
     * Optional label shown in the row's expand toggle tooltip.
     */
    detailLabel?: (row: T) => string;
  };
  /** Disable client sort fallback; force backend sort. */
  serverSideSort?: boolean;
  /** Skeleton row count shown while loading. Default 5. */
  skeletonRows?: number;
}

/**
 * Sort a copy of items by the active column + direction. Used
 * when `load` doesn't accept a `sort` param (serverSideSort =
 * false, the default) — gives instant in-page sort without
 * round-tripping to the backend.
 */
function sortInPlace<T>(
  items: T[],
  column: AppGridColumn<T> | null,
  direction: SortDirection,
): T[] {
  if (!column || !direction || !column.sortAccessor) return items;
  const accessor = column.sortAccessor;
  const sign = direction === "asc" ? 1 : -1;
  return [...items].sort((a, b) => {
    const av = accessor(a);
    const bv = accessor(b);
    if (av == null && bv == null) return 0;
    if (av == null) return 1;
    if (bv == null) return -1;
    if (typeof av === "string" && typeof bv === "string") {
      return sign * av.localeCompare(bv, undefined, { numeric: true });
    }
    return sign * ((av as number) - (bv as number));
  });
}

const alignClass: Record<NonNullable<AppGridColumn<unknown>["align"]>, string> = {
  left: "text-left",
  right: "text-right",
  center: "text-center",
};

export function AppGrid<T>({
  columns,
  load,
  rowKey,
  emptyMessage = "Tidak ada data.",
  toolbar,
  onRowClick,
  initialPageSize = 10,
  expandable,
  serverSideSort = false,
  skeletonRows = 5,
}: AppGridProps<T>) {
  const [page, setPage] = useState(1);
  const [pageSize] = useState(initialPageSize);
  const [search, setSearch] = useState("");
  const [data, setData] = useState<T[]>([]);
  const [total, setTotal] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [sortColumn, setSortColumn] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<SortDirection>(null);

  const reload = async (
    p: number = page,
    s: string = search,
    sortOverride?: { column: string; direction: SortDirection },
  ) => {
    setLoading(true);
    setError(null);
    const currentSort =
      sortOverride ??
      (sortColumn && sortDir
        ? { column: sortColumn, direction: sortDir }
        : null);
    try {
      const res = await load({
        page: p,
        pageSize,
        search: s,
        sort:
          currentSort && currentSort.direction
            ? {
                column: currentSort.column,
                direction: currentSort.direction,
              }
            : null,
      });
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

  // Lazy-load on first mount — ref-guarded so StrictMode dev
  // double-mount doesn't double-fire (and so reset-state-after-error
  // doesn't loop).
  const didInitialLoadRef = useRef(false);
  useEffect(() => {
    if (didInitialLoadRef.current) return;
    didInitialLoadRef.current = true;
    void reload(1, "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    setExpanded(new Set());
    void reload(1, search);
  };

  const toggleExpand = (key: string) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const toggleSort = (col: AppGridColumn<T>) => {
    if (!col.sortable) return;
    // Cycle asc -> desc -> null
    const newDir: SortDirection =
      sortColumn === col.key
        ? sortDir === "asc"
          ? "desc"
          : sortDir === "desc"
            ? null
            : "asc"
        : "asc";
    setSortColumn(newDir ? col.key : null);
    setSortDir(newDir);
    if (serverSideSort) {
      void reload(page, search, newDir ? { column: col.key, direction: newDir } : undefined);
    }
  };

  // Client-sort fallback — applied to the current page's items.
  // When serverSideSort is on, the data is already sorted by the
  // backend (re-sorted during the load call), so we leave it alone.
  const visibleData = useMemo(() => {
    if (serverSideSort || !sortColumn || !sortDir) return data;
    const col = columns.find((c) => c.key === sortColumn);
    if (!col || !col.sortAccessor) return data;
    return sortInPlace(data, col, sortDir);
  }, [data, sortColumn, sortDir, columns, serverSideSort]);

  const hasExpand = !!expandable;

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
            aria-label="Cari data"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full rounded-lg border border-[var(--input)] bg-card py-2 pl-9 pr-3 text-sm text-foreground outline-none transition focus:border-[var(--primary)] focus:ring-2 focus:ring-[var(--ring)] sm:w-72"
          />
        </form>
        <div className="ml-auto flex items-center gap-2">{toolbar}</div>
      </div>

      {/* Desktop table (md+). Mobile collapses to cards below. */}
      <div className="hidden overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--card)] md:block">
        <div className="max-h-[70vh] overflow-auto">
          <table className="w-full border-collapse text-sm">
            <thead className="sticky top-0 z-10">
              <tr className="border-b border-[var(--border)] bg-[var(--surface)]">
                {hasExpand && (
                  <th className="w-10 px-2 py-3" scope="col" />
                )}
                {columns.map((c) => {
                  const sortable = c.sortable;
                  const isActive = sortable && sortColumn === c.key;
                  return (
                    <th
                      key={c.key}
                      scope="col"
                      className={cn(
                        "px-4 py-3 font-semibold text-[var(--muted-foreground)]",
                        alignClass[c.align ?? "left"],
                        c.className,
                      )}
                    >
                      {sortable ? (
                        <button
                          type="button"
                          onClick={() => toggleSort(c)}
                          className="inline-flex items-center gap-1 rounded transition hover:text-[var(--foreground)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--ring)]"
                          aria-label={`Sortir ${c.header}`}
                        >
                          {c.header}
                          {isActive && sortDir === "asc" && <ArrowUp size={12} />}
                          {isActive && sortDir === "desc" && <ArrowDown size={12} />}
                          {!isActive && <ChevronsUpDown size={12} className="opacity-40" />}
                        </button>
                      ) : (
                        c.header
                      )}
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody>
              {loading &&
                Array.from({ length: skeletonRows }).map((_, i) => (
                  <SkeletonRow key={`sk-${i}`} colCount={columns.length + (hasExpand ? 1 : 0)} />),
                )}
              {!loading && error && (
                <tr>
                  <td
                    colSpan={columns.length + (hasExpand ? 1 : 0)}
                    className="px-4 py-8 text-center text-[var(--danger)]"
                  >
                    {error}
                  </td>
                </tr>
              )}
              {!loading && !error && visibleData.length === 0 && (
                <tr>
                  <td
                    colSpan={columns.length + (hasExpand ? 1 : 0)}
                    className="px-4 py-8 text-center text-[var(--muted-foreground)]"
                  >
                    {emptyMessage}
                  </td>
                </tr>
              )}
              {!loading &&
                !error &&
                visibleData.length > 0 &&
                visibleData.map((row) => {
                  const key = rowKey(row);
                  const isExpanded = expanded.has(key);
                  return (
                    <FragmentWithDetail
                      key={key}
                      row={row}
                      columns={columns}
                      rowKey={rowKey}
                      onRowClick={onRowClick}
                      expandable={expandable}
                      isExpanded={isExpanded}
                      toggleExpand={() => toggleExpand(key)}
                    />
                  );
                })}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        <Pagination
          total={total}
          page={page}
          totalPages={totalPages}
          loading={loading}
          pageSize={pageSize}
          currentCount={visibleData.length}
          onPrev={() => {
            const p = Math.max(1, page - 1);
            setPage(p);
            setExpanded(new Set());
            void reload(p, search);
          }}
          onNext={() => {
            const p = Math.min(totalPages, page + 1);
            setPage(p);
            setExpanded(new Set());
            void reload(p, search);
          }}
        />
      </div>

      {/* Mobile card view (< md). Each row becomes a stacked card.
          Only the primary column (first) + actions are shown; the
          rest are stacked as label/value pairs. */}
      <div className="flex flex-col gap-2 md:hidden">
        {loading &&
          Array.from({ length: 3 }).map((_, i) => (
            <div
              key={`sk-m-${i}`}
              className="h-24 animate-pulse rounded-xl border border-[var(--border)] bg-[var(--card)]"
            />
          ))}
        {!loading && !error && visibleData.length === 0 && (
          <div className="rounded-xl border border-dashed border-[var(--border)] bg-[var(--card)] p-6 text-center text-sm text-[var(--muted-foreground)]">
            {emptyMessage}
          </div>
        )}
        {!loading &&
          !error &&
          visibleData.map((row) => {
            const key = rowKey(row);
            const isExpanded = expanded.has(key);
            return (
              <MobileCard
                key={key}
                row={row}
                columns={columns}
                onRowClick={onRowClick}
                expandable={expandable}
                isExpanded={isExpanded}
                toggleExpand={() => toggleExpand(key)}
              />
            );
          })}
        {!loading && !error && visibleData.length > 0 && (
          <Pagination
            total={total}
            page={page}
            totalPages={totalPages}
            loading={loading}
            pageSize={pageSize}
            currentCount={visibleData.length}
            onPrev={() => {
              const p = Math.max(1, page - 1);
              setPage(p);
              setExpanded(new Set());
              void reload(p, search);
            }}
            onNext={() => {
              const p = Math.min(totalPages, page + 1);
              setPage(p);
              setExpanded(new Set());
              void reload(p, search);
            }}
          />
        )}
      </div>
    </div>
  );
}

// ── Sub-components ───────────────────────────────────────────

function SkeletonRow({ colCount }: { colCount: number }) {
  return (
    <tr className="border-b border-[var(--border)] last:border-0">
      {Array.from({ length: colCount }).map((_, i) => (
        <td key={i} className="px-4 py-3">
          <div className="h-4 animate-pulse rounded bg-[var(--surface)]" />
        </td>
      ))}
    </tr>
  );
}

/**
 * Renders a row + (if expanded) a detail row spanning all
 * columns. Extracted so the table body stays readable.
 */
function FragmentWithDetail<T>({
  row,
  columns,
  rowKey: _rowKey,
  onRowClick,
  expandable,
  isExpanded,
  toggleExpand,
}: {
  row: T;
  columns: AppGridColumn<T>[];
  rowKey: (row: T) => string;
  onRowClick?: (row: T) => void;
  expandable?: AppGridProps<T>["expandable"];
  isExpanded: boolean;
  toggleExpand: () => void;
}) {
  const detailLabel = expandable?.detailLabel?.(row);
  return (
    <>
      <tr
        onClick={onRowClick ? () => onRowClick(row) : undefined}
        onKeyDown={
          onRowClick
            ? (e: KeyboardEvent<HTMLTableRowElement>) => {
                if (e.key === "Enter" || e.key === " ") {
                  e.preventDefault();
                  onRowClick(row);
                }
              }
            : undefined
        }
        tabIndex={onRowClick ? 0 : undefined}
        role={onRowClick ? "button" : undefined}
        className={cn(
          "border-b border-[var(--border)] last:border-0 transition-colors hover:bg-[var(--surface)] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--ring)]",
          onRowClick && "cursor-pointer",
        )}
      >
        {expandable && (
          <td className="w-10 px-2 py-3">
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                toggleExpand();
              }}
              aria-label={
                detailLabel
                  ? isExpanded
                    ? `Tutup ${detailLabel}`
                    : `Buka ${detailLabel}`
                  : isExpanded
                    ? "Tutup detail"
                    : "Buka detail"
              }
              aria-expanded={isExpanded}
              className="grid h-7 w-7 place-items-center rounded-md text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] hover:text-[var(--primary)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--ring)]"
            >
              {isExpanded ? (
                <ChevronDown size={14} />
              ) : (
                <ChevronRight size={14} />
              )}
            </button>
          </td>
        )}
        {columns.map((c) => (
          <td
            key={c.key}
            className={cn(
              "px-4 py-3 align-top",
              alignClass[c.align ?? "left"],
              c.className,
            )}
          >
            {c.render(row)}
          </td>
        ))}
      </tr>
      {expandable && isExpanded && (
        <tr className="border-b border-[var(--border)] last:border-0 bg-[var(--surface)]/60">
          <td colSpan={columns.length + 1} className="px-4 py-4">
            <div className="animate-grid-detail-in">
              {expandable.lazy && !isExpanded
                ? null
                : expandable.renderDetail(row, () => toggleExpand())}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

/**
 * Mobile card view — each row becomes a stacked card. First
 * column becomes the card title; remaining columns become
 * label/value pairs underneath. Action column floats right.
 */
function MobileCard<T>({
  row,
  columns,
  onRowClick,
  expandable,
  isExpanded,
  toggleExpand,
}: {
  row: T;
  columns: AppGridColumn<T>[];
  onRowClick?: (row: T) => void;
  expandable?: AppGridProps<T>["expandable"];
  isExpanded: boolean;
  toggleExpand: () => void;
}) {
  const titleCol = columns[0];
  const bodyCols = columns.slice(1);
  return (
    <div className="overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--card)]">
      <div
        onClick={onRowClick ? () => onRowClick(row) : undefined}
        role={onRowClick ? "button" : undefined}
        className={cn(
          "flex items-start gap-3 p-3",
          onRowClick && "cursor-pointer active:bg-[var(--surface)]",
        )}
      >
        {expandable && (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              toggleExpand();
            }}
            aria-label={isExpanded ? "Tutup detail" : "Buka detail"}
            aria-expanded={isExpanded}
            className="mt-0.5 grid h-6 w-6 shrink-0 place-items-center rounded-md text-[var(--muted-foreground)] transition hover:bg-[var(--surface)]"
          >
            {isExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
          </button>
        )}
        <div className="min-w-0 flex-1">
          {titleCol && <div className="font-medium">{titleCol.render(row)}</div>}
          <dl className="mt-1 grid grid-cols-2 gap-x-2 gap-y-1 text-xs">
            {bodyCols.map((c, i) => {
              if (c.hideOnMobile) return null;
              return (
                <div key={c.key ?? i} className="contents">
                  <dt className="text-[var(--muted-foreground)]">{c.header}</dt>
                  <dd className="text-right text-foreground">{c.render(row)}</dd>
                </div>
              );
            })}
          </dl>
        </div>
      </div>
      {expandable && isExpanded && (
        <div className="border-t border-[var(--border)] bg-[var(--surface)]/60 p-3">
          <div className="animate-grid-detail-in">
            {expandable.renderDetail(row, () => toggleExpand())}
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * Pagination footer — shared between desktop table and mobile
 * card list. Extracted so we don't duplicate markup.
 */
function Pagination({
  total,
  page,
  totalPages,
  loading,
  currentCount,
  onPrev,
  onNext,
}: {
  total: number;
  page: number;
  totalPages: number;
  loading: boolean;
  pageSize: number;
  currentCount: number;
  onPrev: () => void;
  onNext: () => void;
}) {
  return (
    <div className="flex items-center justify-between border-t border-[var(--border)] px-4 py-3 text-xs text-[var(--muted-foreground)]">
      <span>
        {total > 0 ? (
          <>Menampilkan {currentCount} dari {total} data</>
        ) : (
          "—"
        )}
      </span>
      <div className="flex items-center gap-1">
        <button
          type="button"
          disabled={page <= 1 || loading}
          onClick={onPrev}
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
          onClick={onNext}
          className="grid h-7 w-7 place-items-center rounded-md border border-[var(--border)] transition hover:bg-[var(--surface)] disabled:cursor-not-allowed disabled:opacity-40"
          aria-label="Halaman berikutnya"
        >
          <ChevronRight size={14} />
        </button>
      </div>
    </div>
  );
}
