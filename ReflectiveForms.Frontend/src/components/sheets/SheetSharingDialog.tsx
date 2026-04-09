import { useState, useMemo } from 'react';
import { X, Globe, Users, Shield, Trash2, UserCog } from 'lucide-react';
import { useEntityList } from '../../hooks/useEntity';
import type { PeekEntity } from '../../types/schema';

export interface SharedUser {
  user: number;
  permission: 'view' | 'edit';
}

export interface SharedRole {
  role: number;
  permission: 'view' | 'edit';
}

export interface SheetSharingState {
  is_public: boolean;
  shared_users: SharedUser[];
  shared_roles: SharedRole[];
}

export interface SheetSharingDialogProps {
  isOwner: boolean;
  sharing: SheetSharingState;
  onChange: (sharing: SheetSharingState) => void;
  onClose: () => void;
  /** Whether the current user can peek_all on the 'users' entity */
  canPeekUsers: boolean;
  /** Whether the current user can peek_all on the 'iam-role' entity */
  canPeekRoles: boolean;
  /** Current author user id (for owner-role users to transfer ownership) */
  authorId?: number;
  /** Called when owner-role user changes the author */
  onAuthorChange?: (newAuthorId: number) => void;
  /** Whether the current user has the system Owner role (can transfer authorship) */
  isSystemOwner?: boolean;
}

export function SheetSharingDialog({ isOwner, sharing, onChange, onClose, canPeekUsers, canPeekRoles, authorId, onAuthorChange, isSystemOwner }: SheetSharingDialogProps) {
  const { data: allUsers } = useEntityList(canPeekUsers ? 'users' : '');
  const { data: allRoles } = useEntityList(canPeekRoles ? 'iam-role' : '');

  // New user to add
  const [newUserId, setNewUserId] = useState<number | ''>('');
  const [newUserPerm, setNewUserPerm] = useState<'view' | 'edit'>('view');

  // New role to add
  const [newRoleId, setNewRoleId] = useState<number | ''>('');
  const [newRolePerm, setNewRolePerm] = useState<'view' | 'edit'>('view');

  const sharedUserIds = useMemo(() => new Set(sharing.shared_users.map((u) => u.user)), [sharing.shared_users]);
  const sharedRoleIds = useMemo(() => new Set(sharing.shared_roles.map((r) => r.role)), [sharing.shared_roles]);

  const availableUsers = useMemo(
    () => (allUsers ?? []).filter((u: PeekEntity) => !sharedUserIds.has(u.id)),
    [allUsers, sharedUserIds],
  );
  const availableRoles = useMemo(
    () => (allRoles ?? []).filter((r: PeekEntity) => !sharedRoleIds.has(r.id)),
    [allRoles, sharedRoleIds],
  );

  const addUser = () => {
    if (newUserId === '') return;
    onChange({
      ...sharing,
      shared_users: [...sharing.shared_users, { user: newUserId, permission: newUserPerm }],
    });
    setNewUserId('');
    setNewUserPerm('view');
  };

  const removeUser = (userId: number) => {
    onChange({
      ...sharing,
      shared_users: sharing.shared_users.filter((u) => u.user !== userId),
    });
  };

  const updateUserPerm = (userId: number, perm: 'view' | 'edit') => {
    onChange({
      ...sharing,
      shared_users: sharing.shared_users.map((u) => (u.user === userId ? { ...u, permission: perm } : u)),
    });
  };

  const addRole = () => {
    if (newRoleId === '') return;
    onChange({
      ...sharing,
      shared_roles: [...sharing.shared_roles, { role: newRoleId, permission: newRolePerm }],
    });
    setNewRoleId('');
    setNewRolePerm('view');
  };

  const removeRole = (roleId: number) => {
    onChange({
      ...sharing,
      shared_roles: sharing.shared_roles.filter((r) => r.role !== roleId),
    });
  };

  const updateRolePerm = (roleId: number, perm: 'view' | 'edit') => {
    onChange({
      ...sharing,
      shared_roles: sharing.shared_roles.map((r) => (r.role === roleId ? { ...r, permission: perm } : r)),
    });
  };

  const getUserDisplay = (userId: number) => {
    const u = (allUsers ?? []).find((x: PeekEntity) => x.id === userId);
    return u ? (u.title || u.author || `User #${userId}`) : `User #${userId}`;
  };

  const getRoleDisplay = (roleId: number) => {
    const r = (allRoles ?? []).find((x: PeekEntity) => x.id === roleId);
    return r ? (r.title || `Role #${roleId}`) : `Role #${roleId}`;
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40" onClick={onClose}>
      <div
        className="bg-white dark:bg-gray-800 rounded-xl shadow-2xl w-[520px] max-h-[80vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="px-5 py-4 border-b border-gray-200 dark:border-gray-700 flex items-center justify-between">
          <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Sharing Settings</h3>
          <button
            onClick={onClose}
            className="p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 rounded"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4 space-y-5">
          {/* Public toggle */}
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Globe className="w-4 h-4 text-gray-400" />
              <div>
                <p className="text-sm font-medium text-gray-700 dark:text-gray-300">Public</p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Anyone with sheet permissions can view
                </p>
              </div>
            </div>
            <label className="relative inline-flex items-center cursor-pointer">
              <input
                type="checkbox"
                checked={sharing.is_public}
                disabled={!isOwner}
                onChange={(e) => onChange({ ...sharing, is_public: e.target.checked })}
                className="sr-only peer"
              />
              <div className="w-9 h-5 bg-gray-200 peer-focus:ring-2 peer-focus:ring-primary-300 rounded-full peer dark:bg-gray-600 peer-checked:after:translate-x-full rtl:peer-checked:after:-translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:start-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all dark:border-gray-600 peer-checked:bg-primary-600 peer-disabled:opacity-50" />
            </label>
          </div>

          {/* Author (ownership transfer) — only for system Owner role users */}
          {isSystemOwner && canPeekUsers && authorId !== undefined && onAuthorChange && (
            <div>
              <div className="flex items-center gap-2 mb-2">
                <UserCog className="w-4 h-4 text-gray-400" />
                <p className="text-sm font-medium text-gray-700 dark:text-gray-300">Sheet Author</p>
              </div>
              <select
                value={authorId}
                onChange={(e) => onAuthorChange(Number(e.target.value))}
                className="w-full text-sm border border-gray-300 dark:border-gray-600 rounded-lg px-3 py-1.5 bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300"
              >
                {(allUsers ?? []).map((u: PeekEntity) => (
                  <option key={u.id} value={u.id}>
                    {u.title || u.author || `User #${u.id}`}
                  </option>
                ))}
              </select>
              <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                Changing the author transfers ownership of this sheet.
              </p>
            </div>
          )}

          {/* Shared Users */}
          {canPeekUsers && <div>
            <div className="flex items-center gap-2 mb-2">
              <Users className="w-4 h-4 text-gray-400" />
              <p className="text-sm font-medium text-gray-700 dark:text-gray-300">Shared with Users</p>
            </div>

            {sharing.shared_users.length > 0 && (
              <div className="space-y-1.5 mb-2">
                {sharing.shared_users.map((su) => (
                  <div key={su.user} className="flex items-center gap-2 text-sm bg-gray-50 dark:bg-gray-750 rounded-lg px-3 py-1.5">
                    <span className="flex-1 truncate text-gray-700 dark:text-gray-300">
                      {getUserDisplay(su.user)}
                    </span>
                    <select
                      value={su.permission}
                      disabled={!isOwner}
                      onChange={(e) => updateUserPerm(su.user, e.target.value as 'view' | 'edit')}
                      className="text-xs border border-gray-300 dark:border-gray-600 rounded px-2 py-1 bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300"
                    >
                      <option value="view">View</option>
                      <option value="edit">Edit</option>
                    </select>
                    {isOwner && (
                      <button
                        onClick={() => removeUser(su.user)}
                        className="p-1 text-gray-400 hover:text-red-500"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    )}
                  </div>
                ))}
              </div>
            )}

            {isOwner && (
              <div className="flex items-center gap-2">
                <select
                  value={newUserId}
                  onChange={(e) => setNewUserId(e.target.value ? Number(e.target.value) : '')}
                  className="flex-1 text-sm border border-gray-300 dark:border-gray-600 rounded-lg px-3 py-1.5 bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300"
                >
                  <option value="">Select user...</option>
                  {availableUsers.map((u: PeekEntity) => (
                    <option key={u.id} value={u.id}>
                      {u.title || u.author || `User #${u.id}`}
                    </option>
                  ))}
                </select>
                <select
                  value={newUserPerm}
                  onChange={(e) => setNewUserPerm(e.target.value as 'view' | 'edit')}
                  className="text-xs border border-gray-300 dark:border-gray-600 rounded px-2 py-1.5 bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300"
                >
                  <option value="view">View</option>
                  <option value="edit">Edit</option>
                </select>
                <button
                  onClick={addUser}
                  disabled={newUserId === ''}
                  className="px-3 py-1.5 text-xs bg-primary-600 text-white rounded-lg hover:bg-primary-700 disabled:opacity-50 transition-colors"
                >
                  Add
                </button>
              </div>
            )}
          </div>}

          {/* Shared Roles */}
          {canPeekRoles && <div>
            <div className="flex items-center gap-2 mb-2">
              <Shield className="w-4 h-4 text-gray-400" />
              <p className="text-sm font-medium text-gray-700 dark:text-gray-300">Shared with Roles</p>
            </div>

            {sharing.shared_roles.length > 0 && (
              <div className="space-y-1.5 mb-2">
                {sharing.shared_roles.map((sr) => (
                  <div key={sr.role} className="flex items-center gap-2 text-sm bg-gray-50 dark:bg-gray-750 rounded-lg px-3 py-1.5">
                    <span className="flex-1 truncate text-gray-700 dark:text-gray-300">
                      {getRoleDisplay(sr.role)}
                    </span>
                    <select
                      value={sr.permission}
                      disabled={!isOwner}
                      onChange={(e) => updateRolePerm(sr.role, e.target.value as 'view' | 'edit')}
                      className="text-xs border border-gray-300 dark:border-gray-600 rounded px-2 py-1 bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300"
                    >
                      <option value="view">View</option>
                      <option value="edit">Edit</option>
                    </select>
                    {isOwner && (
                      <button
                        onClick={() => removeRole(sr.role)}
                        className="p-1 text-gray-400 hover:text-red-500"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    )}
                  </div>
                ))}
              </div>
            )}

            {isOwner && (
              <div className="flex items-center gap-2">
                <select
                  value={newRoleId}
                  onChange={(e) => setNewRoleId(e.target.value ? Number(e.target.value) : '')}
                  className="flex-1 text-sm border border-gray-300 dark:border-gray-600 rounded-lg px-3 py-1.5 bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300"
                >
                  <option value="">Select role...</option>
                  {availableRoles.map((r: PeekEntity) => (
                    <option key={r.id} value={r.id}>
                      {r.title || `Role #${r.id}`}
                    </option>
                  ))}
                </select>
                <select
                  value={newRolePerm}
                  onChange={(e) => setNewRolePerm(e.target.value as 'view' | 'edit')}
                  className="text-xs border border-gray-300 dark:border-gray-600 rounded px-2 py-1.5 bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300"
                >
                  <option value="view">View</option>
                  <option value="edit">Edit</option>
                </select>
                <button
                  onClick={addRole}
                  disabled={newRoleId === ''}
                  className="px-3 py-1.5 text-xs bg-primary-600 text-white rounded-lg hover:bg-primary-700 disabled:opacity-50 transition-colors"
                >
                  Add
                </button>
              </div>
            )}
          </div>}
        </div>

        {/* Footer */}
        <div className="px-5 py-3 border-t border-gray-200 dark:border-gray-700 flex justify-end">
          <button
            onClick={onClose}
            className="px-4 py-1.5 text-sm bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
          >
            Done
          </button>
        </div>
      </div>
    </div>
  );
}
