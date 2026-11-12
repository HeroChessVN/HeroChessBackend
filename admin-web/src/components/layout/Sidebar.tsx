// Vai trò file: Sidebar navy đậm — điều hướng chính của AdminWeb.
// Mục menu được ẩn theo quyền của tài khoản (chỉ để ẩn/hiện; server vẫn là nơi kiểm tra thật).

import { NavLink } from 'react-router-dom';
import {
  BarChart3,
  Gift,
  LayoutDashboard,
  ScrollText,
  ShieldCheck,
  ShoppingBag,
  Swords,
  Users,
  UsersRound
} from 'lucide-react';
import type { ComponentType } from 'react';
import { hasPermission, PERMISSION_ADMIN, PERMISSION_ANALYTICS, PERMISSION_AUDIT, PERMISSION_GIFTCODE, PERMISSION_HERO, PERMISSION_PLAYER, PERMISSION_READ, PERMISSION_SHOP, type PermissionSet } from '../../lib/permissions';

interface NavItem {
  to: string;
  label: string;
  icon: ComponentType<{ size?: number; className?: string }>;
  domain: string;
  end?: boolean;
}

const items: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, domain: PERMISSION_READ, end: true },
  { to: '/heroes', label: 'Hero', icon: Swords, domain: PERMISSION_HERO },
  { to: '/shop', label: 'Cửa hàng', icon: ShoppingBag, domain: PERMISSION_SHOP },
  { to: '/players', label: 'Người chơi', icon: Users, domain: PERMISSION_PLAYER },
  { to: '/gift-codes', label: 'Gift Code', icon: Gift, domain: PERMISSION_GIFTCODE },
  { to: '/matches', label: 'Trận đấu', icon: Swords, domain: PERMISSION_ANALYTICS },
  { to: '/analytics', label: 'Phân tích', icon: BarChart3, domain: PERMISSION_ANALYTICS },
  { to: '/admin', label: 'Tài khoản Admin', icon: ShieldCheck, domain: PERMISSION_ADMIN },
  { to: '/audit', label: 'Nhật ký thao tác', icon: ScrollText, domain: PERMISSION_AUDIT }
];

interface SidebarProps {
  permissions: PermissionSet;
  onNavigate?: () => void;
}

export function Sidebar({ permissions, onNavigate }: SidebarProps) {
  return (
    <nav className="flex h-full w-full flex-col bg-navy-900" aria-label="Điều hướng quản trị">
      <div className="flex items-center gap-2.5 border-b border-navy-700 px-5 py-4">
        <span className="grid size-9 place-items-center rounded-lg bg-gold-500/15 text-lg text-gold-500" aria-hidden>
          ♞
        </span>
        <div className="min-w-0">
          <p className="truncate text-sm font-bold tracking-tight text-ink">Hero Chess</p>
          <p className="truncate text-xs text-ink-dim">Game Operations Console</p>
        </div>
      </div>

      <ul className="flex-1 space-y-0.5 overflow-y-auto px-3 py-4">
        {items
          .filter(item => hasPermission(permissions, item.domain))
          .map(item => {
            const Icon = item.icon;
            return (
              <li key={item.to}>
                <NavLink
                  to={item.to}
                  end={item.end}
                  onClick={onNavigate}
                  className={({ isActive }) =>
                    `flex items-center gap-2.5 rounded-lg px-3 py-2.5 text-sm transition-colors ${
                      isActive ? 'bg-gold-500/15 font-semibold text-gold-400' : 'text-ink-muted hover:bg-navy-800 hover:text-ink'
                    }`
                  }
                >
                  <Icon size={17} />
                  {item.label}
                </NavLink>
              </li>
            );
          })}
      </ul>

      <div className="border-t border-navy-700 px-5 py-3">
        <p className="flex items-center gap-1.5 text-xs text-ink-dim">
          <UsersRound size={13} />
          Hero Chess Admin · v0.1
        </p>
        <p className="mt-1 flex items-center gap-1.5 text-xs text-ink-dim">
          <ScrollText size={13} />
          Thao tác ghi đều được ghi nhật ký
        </p>
      </div>
    </nav>
  );
}
