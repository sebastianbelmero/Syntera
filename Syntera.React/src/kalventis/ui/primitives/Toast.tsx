import { Toaster as SonnerToaster, type ToasterProps } from "sonner";

/**
 * Toast notification container.
 * Place this once at the root of your app.
 *
 * Usage:
 *   import { Toaster, toast } from "@sebastianbelmero/kalventis-ui";
 *
 *   // In root:
 *   <Toaster richColors position="top-right" />
 *
 *   // Anywhere in components:
 *   toast.success("Saved!");
 *   toast.error("Failed to save");
 */
export const Toaster = ({ ...props }: ToasterProps) => {
  return (
    <SonnerToaster
      className="toaster group"
      style={
        {
          "--normal-bg": "var(--popover)",
          "--normal-text": "var(--popover-foreground)",
          "--normal-border": "var(--border)",
        } as React.CSSProperties
      }
      {...props}
    />
  );
};

export { toast } from "sonner";
