import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Home, TrendingUp, Target, FileText, LogOut, User, Settings, Upload, Crosshair, Brain, FileWarning, Users, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useAuth } from '@/lib/AuthContext';

const baseNavigation = [
  { name: 'Dashboard', href: '/dashboard', icon: Home },
  { name: 'Trades', href: '/trades', icon: TrendingUp },
  { name: 'Strategies', href: '/strategies', icon: Target },
  { name: 'Reports', href: '/reports', icon: FileText },
  { name: 'Import', href: '/import', icon: Upload },
  { name: 'TrailBlazer', href: '/trailblazer', icon: Crosshair },
  { name: 'Settings', href: '/settings', icon: Settings },
];

interface AppSidebarProps {
  open?: boolean;
  onClose?: () => void;
}

export function AppSidebar({ open = false, onClose }: AppSidebarProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout, isAdmin } = useAuth();

  const navigation = [
    ...baseNavigation,
    ...(isAdmin
      ? [
          { name: 'AI Insights', href: '/ai-insights', icon: Brain },
          { name: 'Users', href: '/admin/users', icon: Users },
          { name: 'Error Logs', href: '/admin/error-logs', icon: FileWarning },
        ]
      : []),
  ];

  const handleLogout = async () => {
    await logout();
    navigate('/');
    onClose?.();
  };

  const handleNavClick = () => {
    onClose?.();
  };

  const sidebarContent = (
    <div className="flex h-full w-64 flex-col bg-card border-r">
      <div className="flex h-16 items-center justify-between border-b px-4 lg:px-6">
        <Link to="/dashboard" className="flex items-center space-x-2" onClick={handleNavClick}>
          <TrendingUp className="h-6 w-6 text-primary" />
          <span className="text-xl font-bold text-primary">TradeTracker</span>
        </Link>
        {onClose && (
          <Button variant="ghost" size="icon" className="lg:hidden h-11 w-11 min-h-[44px] min-w-[44px]" onClick={onClose} aria-label="Close menu">
            <X className="h-5 w-5" />
          </Button>
        )}
      </div>
      
      <nav className="flex-1 space-y-1 px-3 py-4">
        {navigation.map((item) => {
          const isActive = location.pathname.startsWith(item.href);
          return (
            <Link
              key={item.name}
              to={item.href}
              onClick={handleNavClick}
              className={cn(
                'flex items-center space-x-3 rounded-lg px-3 py-3 text-sm font-medium transition-colors min-h-[44px]',
                isActive
                  ? 'bg-primary text-primary-foreground'
                  : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
              )}
            >
              <item.icon className="h-5 w-5 shrink-0" />
              <span>{item.name}</span>
            </Link>
          );
        })}
      </nav>

      {/* User Section */}
      <div className="border-t p-4 space-y-2">
        {user && (
          <div className="flex items-center space-x-2 px-2 py-2 text-sm text-muted-foreground">
            <User className="h-4 w-4" />
            <span className="truncate">{user.firstName} {user.lastName}</span>
          </div>
        )}
        <Button
          variant="ghost"
          className="w-full justify-start text-muted-foreground hover:text-destructive min-h-[44px]"
          onClick={handleLogout}
        >
          <LogOut className="h-4 w-4 mr-2" />
          Sign Out
        </Button>
      </div>
    </div>
  );

  return (
    <>
      {/* Desktop: always visible */}
      <aside className="hidden lg:flex shrink-0">{sidebarContent}</aside>
      {/* Mobile: overlay drawer */}
      {onClose && (
        <>
          <div
            className={cn(
              'fixed inset-0 z-40 bg-black/50 transition-opacity lg:hidden',
              open ? 'opacity-100' : 'opacity-0 pointer-events-none'
            )}
            onClick={onClose}
            aria-hidden="true"
          />
          <aside
            className={cn(
              'fixed inset-y-0 left-0 z-50 w-64 transform transition-transform duration-200 ease-out lg:hidden',
              open ? 'translate-x-0' : '-translate-x-full'
            )}
          >
            {sidebarContent}
          </aside>
        </>
      )}
    </>
  );
}

