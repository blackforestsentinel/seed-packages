import { useSeedUser } from '@blackforestsentinel/seed-web-auth/react';
import { cx } from '../utils.js';

export interface RoleBadgeProps {
  /** Wert der Rolle wie in project.yaml, z. B. Admin. */
  role: string;
  /** Anzeigename; Default aus roleDetails der angemeldeten Person, sonst der Wert. */
  displayName?: string;
  /** tint (Default), filled oder outline wie Fluent-Badges. */
  appearance?: 'tint' | 'filled' | 'outline';
  size?: 'small' | 'medium';
  className?: string;
}

/** Rolle als kleines Etikett, etwa „Administrator“. */
export function RoleBadge({ role, displayName, appearance = 'tint', size = 'medium', className }: RoleBadgeProps) {
  const { user } = useSeedUser();
  const text = displayName ?? user?.roleDetails?.find((detail) => detail.value === role)?.displayName ?? role;
  return (
    <span className={cx('seed-role-badge', className)} data-role={role} data-appearance={appearance} data-size={size}>
      {text}
    </span>
  );
}
