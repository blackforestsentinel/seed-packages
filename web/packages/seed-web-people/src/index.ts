// Komponenten
export { AccessDenied } from './components/AccessDenied.js';
export type { AccessDeniedContact, AccessDeniedProps } from './components/AccessDenied.js';
export { Avatar } from './components/Avatar.js';
export type { AvatarProps } from './components/Avatar.js';
export { AvatarGroup } from './components/AvatarGroup.js';
export type { AvatarGroupPerson, AvatarGroupProps } from './components/AvatarGroup.js';
export { CurrentUserMenu } from './components/CurrentUserMenu.js';
export type { CurrentUserMenuItem, CurrentUserMenuProps } from './components/CurrentUserMenu.js';
export { PeoplePicker } from './components/PeoplePicker.js';
export type { PeoplePickerLabels, PeoplePickerOptionState, PeoplePickerProps } from './components/PeoplePicker.js';
export { PersonCard } from './components/PersonCard.js';
export type { PersonCardProps } from './components/PersonCard.js';
export { PersonLabel } from './components/PersonLabel.js';
export type { PersonLabelProps } from './components/PersonLabel.js';
export { PresenceBadge } from './components/PresenceBadge.js';
export type { PresenceBadgeProps } from './components/PresenceBadge.js';
export { RoleBadge } from './components/RoleBadge.js';
export type { RoleBadgeProps } from './components/RoleBadge.js';
export { SignInPage } from './components/SignInPage.js';
export type { SignInPageProps } from './components/SignInPage.js';

// Provider und Hooks
export { createDirectory, DirectoryProvider, useDirectory, useDirectoryAuth, useSignOut } from './provider.js';
export type { DirectoryAuth, DirectoryProviderProps } from './provider.js';
export { useAppRoleMembers, useCurrentUser, usePeopleSearch, usePerson, usePhoto, usePresence } from './hooks.js';
export type {
  AppRoleMembersState,
  CurrentUser,
  CurrentUserState,
  PeopleSearchOptions,
  PeopleSearchState,
  PersonState,
  PhotoState,
  PresenceState,
} from './hooks.js';

// Verzeichnis ohne React
export { createEmptyDirectory, createLocalDirectory, DirectoryError, matchesSearch, photoSizeFor } from './directory.js';
export type { Directory, SearchOptions } from './directory.js';
export { createGraphDirectory, toPresence } from './graph-directory.js';
export type { DirectoryTtl, GraphDirectoryOptions } from './graph-directory.js';
export { GraphError } from './graph-client.js';
export { AVATAR_COLORS, avatarColor, initials, PRESENCE_LABELS, teamsChatUrl } from './utils.js';
export type { AvatarColor } from './utils.js';
export type { DirectoryMode, LoadStatus, Person, PhotoSize, Presence, PresenceStatus } from './types.js';
export type { SeedDirectoryConfig, SeedLocalDirectoryConfig, SeedLocalPerson } from '@blackforestsentinel/seed-web-auth';
