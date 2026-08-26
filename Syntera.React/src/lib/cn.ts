import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/**
 * `cn` — small utility to compose conditional class names while
 * letting tailwind-merge dedupe conflicting Tailwind utilities.
 * Used everywhere a component accepts a `className` prop.
 */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
