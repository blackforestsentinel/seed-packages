// Einfache Icons im 20er-Raster wie Fluent, als Inline-SVG ohne Abhängigkeit. Farbe über
// currentColor, also aus dem CSS der Komponente.
import type { ReactNode } from 'react';

interface IconProps {
  className?: string;
}

function Icon({ className, children }: IconProps & { children: ReactNode }) {
  return (
    <svg
      className={className}
      viewBox="0 0 20 20"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  );
}

export function SignOutIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M8 3.5H5.5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2H8" />
      <path d="M12.5 6.5 16 10l-3.5 3.5" />
      <path d="M16 10H8" />
    </Icon>
  );
}

export function ChevronDownIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="m5.5 7.75 4.5 4.5 4.5-4.5" />
    </Icon>
  );
}

export function DismissIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="m5.5 5.5 9 9M14.5 5.5l-9 9" />
    </Icon>
  );
}

export function PersonIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="10" cy="7" r="3.25" />
      <path d="M4 16.5c.8-2.9 3.2-4.5 6-4.5s5.2 1.6 6 4.5" />
    </Icon>
  );
}

/** Anwendung (Service Principal vom Typ Application). */
export function AppIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <rect x="3.5" y="3.5" width="5" height="5" rx="1" />
      <rect x="11.5" y="3.5" width="5" height="5" rx="1" />
      <rect x="3.5" y="11.5" width="5" height="5" rx="1" />
      <rect x="11.5" y="11.5" width="5" height="5" rx="1" />
    </Icon>
  );
}

/** Verwaltete Identität (Service Principal vom Typ ManagedIdentity). */
export function KeyIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="6.75" cy="10" r="3.25" />
      <path d="M10 10h7M14.5 10v2.5M16.75 10v2" />
    </Icon>
  );
}

export function MailIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <rect x="2.75" y="4.75" width="14.5" height="10.5" rx="2" />
      <path d="m3.5 6.5 6.5 4.5 6.5-4.5" />
    </Icon>
  );
}

export function ChatIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M10 3.5c3.9 0 7 2.7 7 6s-3.1 6-7 6c-.9 0-1.8-.15-2.6-.4L4 16.5l.9-3.1C3.7 12.3 3 11 3 9.5c0-3.3 3.1-6 7-6Z" />
    </Icon>
  );
}

export function LockIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <rect x="4.5" y="9" width="11" height="8" rx="2" />
      <path d="M7 9V6.5a3 3 0 0 1 6 0V9" />
    </Icon>
  );
}
