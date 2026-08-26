import { useState } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Activity } from "lucide-react";
import { useAuthStore } from "../../store/authStore";
import { authApi } from "../../api/auth";
import { ApiError } from "../../api/client";

export default function LoginPage() {
  const [email, setEmail] = useState("admin@syntera.local");
  const [password, setPassword] = useState("ChangeMe!Strong#1");
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const login = useAuthStore((s) => s.login);
  const isAuthed = useAuthStore((s) => s.isAuthenticated());

  if (isAuthed) {
    const from = (location.state as { from?: string } | null)?.from ?? "/dashboard";
    return <Navigate to={from} replace />;
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) {
      toast.error("Email dan password wajib diisi.");
      return;
    }
    setLoading(true);
    try {
      const res = await authApi.login({ email, password });
      login(res);
      toast.success(`Selamat datang, ${res.profile.fullName ?? res.profile.email}!`);
      navigate("/dashboard", { replace: true });
    } catch (err) {
      const apiErr = err as ApiError;
      toast.error(apiErr.message ?? "Login gagal. Periksa kredensial Anda.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="grid min-h-screen lg:grid-cols-2">
      {/* Brand panel — left side on desktop */}
      <div
        className="relative hidden flex-col justify-between p-12 text-white lg:flex"
        style={{
          background:
            "linear-gradient(135deg, var(--primary) 0%, #00563b 60%, #0f3d24 100%)",
        }}
      >
        <div className="flex items-center gap-3 text-2xl font-bold tracking-tight">
          <div className="rounded-xl bg-white/15 p-2 backdrop-blur">
            <Activity size={28} />
          </div>
          <div>
            Syntera
            <div className="text-xs font-normal opacity-70">
              Pharmaceutical Commerce Suite
            </div>
          </div>
        </div>

        <div className="space-y-6 text-lg">
          <p className="max-w-md text-3xl font-semibold leading-snug">
            Vital Science, <span className="text-[var(--accent)]">Vital Commerce.</span>
          </p>
          <p className="max-w-md text-base opacity-80">
            Manajemen inventaris obat, penjualan apotek, dan pelacakan
            kadaluarsa — terintegrasi dengan standar BPOM & Kemenkes.
          </p>
        </div>

        <div className="flex items-center gap-4 text-xs opacity-60">
          <span>© 2026 Syntera</span>
          <span>•</span>
          <span>Powered by .NET 10 + React 19</span>
        </div>

        {/* Decorative geometry */}
        <div
          aria-hidden
          className="pointer-events-none absolute right-[-200px] top-[-200px] h-[480px] w-[480px] rounded-full opacity-20"
          style={{
            background:
              "radial-gradient(circle, var(--accent) 0%, transparent 70%)",
          }}
        />
      </div>

      {/* Form panel — right side */}
      <div className="flex items-center justify-center p-6 sm:p-12">
        <form
          onSubmit={handleSubmit}
          className="w-full max-w-sm space-y-6"
          aria-label="Login form"
        >
          <header className="space-y-2 text-center">
            <div className="mx-auto w-fit rounded-xl bg-[var(--surface)] p-3 text-[var(--primary)] lg:hidden">
              <Activity size={28} />
            </div>
            <h1 className="text-2xl font-bold tracking-tight text-[var(--foreground)]">
              Masuk ke akun Anda
            </h1>
            <p className="text-sm text-[var(--muted-foreground)]">
              Gunakan kredensial yang terdaftar pada sistem Syntera.
            </p>
          </header>

          <div className="space-y-3">
            <label className="block text-sm font-medium text-[var(--foreground)]">
              Email
              <input
                type="email"
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="mt-1 w-full rounded-lg border border-[var(--input)] bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-[var(--primary)] focus:ring-2 focus:ring-[var(--ring)]"
                placeholder="anda@apotek.co.id"
                required
              />
            </label>

            <label className="block text-sm font-medium text-[var(--foreground)]">
              Password
              <input
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="mt-1 w-full rounded-lg border border-[var(--input)] bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-[var(--primary)] focus:ring-2 focus:ring-[var(--ring)]"
                placeholder="••••••••"
                required
              />
            </label>
          </div>

          <button
            type="submit"
            disabled={loading}
            className="w-full rounded-lg bg-[var(--primary)] px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-[var(--primary-hover)] disabled:cursor-not-allowed disabled:opacity-60"
          >
            {loading ? "Memverifikasi…" : "Masuk"}
          </button>

          <p className="text-center text-xs text-[var(--muted-foreground)]">
            Demo: <code className="font-mono">admin@syntera.local</code> /{" "}
            <code className="font-mono">ChangeMe!Strong#1</code>
          </p>
        </form>
      </div>
    </div>
  );
}
