import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Home, TrendingUp, Target, FileText, LogOut, User, Settings, Upload, Crosshair, Brain } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useAuth } from '@/lib/AuthContext';

const navigation = [
  { name: 'Dashboard', href: '/dashboard', icon: Home },
  { name: 'Trades', href: '/trades', icon: TrendingUp },
  { name: 'Strategies', href: '/strategies', icon: Target },
  { name: 'Reports', href: '/reports', icon: FileText },
  { name: 'Import', href: '/import', icon: Upload },
  { name: 'TrailBlazer', href: '/trailblazer', icon: Crosshair },
  { name: 'AI Insights', href: '/ai-insights', icon: Brain },
  { name: 'Settings', href: '/settings', icon: Settings },
];

export function AppSidebar() {
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout } = useAuth();

  const handleLogout = async () => {
    await logout();
    navigate('/');
  };

  return (
    <div className="flex h-full w-64 flex-col bg-card border-r">
      <div className="flex h-16 items-center border-b px-6">
        <Link to="/dashboard" className="flex items-center space-x-2">
          <TrendingUp className="h-6 w-6 text-primary" />
          <span className="text-xl font-bold text-primary">TradeTracker</span>
        </Link>
      </div>
      
      <nav className="flex-1 space-y-1 px-3 py-4">
        {navigation.map((item) => {
          const isActive = location.pathname.startsWith(item.href);
          return (
            <Link
              key={item.name}
              to={item.href}
              className={cn(
                'flex items-center space-x-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                isActive
                  ? 'bg-primary text-primary-foreground'
                  : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
              )}
            >
              <item.icon className="h-5 w-5" />
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
          className="w-full justify-start text-muted-foreground hover:text-destructive"
          onClick={handleLogout}
        >
          <LogOut className="h-4 w-4 mr-2" />
          Sign Out
        </Button>
      </div>
    </div>
  );
}

