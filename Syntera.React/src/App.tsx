import { Routes, Route, Navigate } from "react-router-dom";
import { AdminLayout, type MenuItem } from "@sebastianbelmero/kalventis-ui";
import {
  LayoutDashboard,
  Package,
  Tags,
  Truck,
  Boxes,
  ShoppingCart,
  Users,
  Settings,
} from "lucide-react";

import { RequireAuth, RequireRole } from "./routes/guards";
import { useAuthStore } from "./store/authStore";
import LoginPage from "./pages/auth/LoginPage";
import DashboardPage from "./pages/dashboard/DashboardPage";
import ProductsPage from "./pages/catalog/ProductsPage";
import CategoriesPage from "./pages/catalog/CategoriesPage";
import SuppliersPage from "./pages/parties/SuppliersPage";
import InventoryPage from "./pages/inventory/InventoryPage";
import SalesPage from "./pages/sales/SalesPage";
import CustomersPage from "./pages/parties/CustomersPage";
import SettingsPage from "./pages/settings/SettingsPage";

const menu: MenuItem[] = [
  { label: "Dashboard", path: "/dashboard", icon: <LayoutDashboard size={18} /> },
  { label: "Produk", path: "/products", icon: <Package size={18} /> },
  { label: "Kategori", path: "/categories", icon: <Tags size={18} /> },
  { label: "Pemasok", path: "/suppliers", icon: <Truck size={18} /> },
  { label: "Persediaan", path: "/inventory", icon: <Boxes size={18} /> },
  { label: "Penjualan", path: "/sales", icon: <ShoppingCart size={18} /> },
  { label: "Pelanggan", path: "/customers", icon: <Users size={18} /> },
  { label: "Pengaturan", path: "/settings", icon: <Settings size={18} /> },
];

export default function App() {
  const profile = useAuthStore((s) => s.profile);
  const logout = useAuthStore((s) => s.logout);

  return (
    <Routes>
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="/login" element={<LoginPage />} />

      <Route
        element={
          <RequireAuth>
            <AdminLayout
              title="Syntera — Pharmaceutical Commerce Suite"
              menuItems={menu}
              user={
                profile
                  ? {
                      name: profile.fullName ?? profile.email,
                      email: profile.email,
                      role: profile.roles.join(", "),
                    }
                  : undefined
              }
              onLogout={() => {
                logout();
                window.location.href = "/login";
              }}
            />
          </RequireAuth>
        }
      >
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route
          path="/products"
          element={
            <RequireRole roles={["Admin", "Pharmacist"]}>
              <ProductsPage />
            </RequireRole>
          }
        />
        <Route path="/categories" element={<CategoriesPage />} />
        <Route path="/suppliers" element={<SuppliersPage />} />
        <Route path="/inventory" element={<InventoryPage />} />
        <Route path="/sales" element={<SalesPage />} />
        <Route path="/customers" element={<CustomersPage />} />
        <Route path="/settings" element={<SettingsPage />} />
      </Route>

      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}
