import { useEffect, useState } from 'react';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ConfirmDialog } from '@/components/ui/dialog';
import { StrategyFormModal } from '@/components/StrategyFormModal';
import { Plus, TrendingUp, TrendingDown, Activity, Target, Edit, Trash2 } from 'lucide-react';
import { api } from '@/lib/api';
import { Strategy } from '@/lib/types';
import { formatCurrency } from '@/lib/utils';

export default function Strategies() {
  const [strategies, setStrategies] = useState<Strategy[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [formModalOpen, setFormModalOpen] = useState(false);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [selectedStrategy, setSelectedStrategy] = useState<Strategy | null>(null);

  useEffect(() => {
    loadStrategies();
  }, []);

  const loadStrategies = async () => {
    try {
      const response = await api.getStrategies();
      setStrategies(response.data || response || []);
      setError(null);
    } catch (error) {
      console.error('Failed to load strategies:', error);
      setError('Failed to load strategies. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  if (loading) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-96">
          <div className="text-muted-foreground">Loading strategies...</div>
        </div>
      </AppLayout>
    );
  }

  if (error) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-96">
          <div className="text-center">
            <p className="text-destructive mb-4">{error}</p>
            <Button onClick={loadStrategies}>Retry</Button>
          </div>
        </div>
      </AppLayout>
    );
  }

  const handleCreateStrategy = () => {
    setSelectedStrategy(null);
    setFormModalOpen(true);
  };

  const handleEditStrategy = (strategy: Strategy) => {
    setSelectedStrategy(strategy);
    setFormModalOpen(true);
  };

  const handleDeleteStrategy = (strategy: Strategy) => {
    setSelectedStrategy(strategy);
    setDeleteDialogOpen(true);
  };

  const confirmDelete = async () => {
    if (!selectedStrategy) return;

    try {
      await api.deleteStrategy(selectedStrategy.id);
      loadStrategies();
    } catch (error) {
      console.error('Failed to delete strategy:', error);
      alert('Failed to delete strategy. Please try again.');
    }
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Modals */}
        <StrategyFormModal
          open={formModalOpen}
          onOpenChange={setFormModalOpen}
          strategy={selectedStrategy}
          onSuccess={loadStrategies}
        />

        <ConfirmDialog
          open={deleteDialogOpen}
          onOpenChange={setDeleteDialogOpen}
          title="Delete Strategy"
          description={`Are you sure you want to delete "${selectedStrategy?.name}"? This will not delete associated trades, but they will no longer be linked to this strategy.`}
          confirmText="Delete"
          cancelText="Cancel"
          onConfirm={confirmDelete}
          variant="destructive"
        />

        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-primary">Strategies</h1>
            <p className="text-muted-foreground">Manage and analyze your trading strategies</p>
          </div>
          <Button onClick={handleCreateStrategy}>
            <Plus className="mr-2 h-4 w-4" />
            Add Strategy
          </Button>
        </div>

        {/* Strategies Grid */}
        {strategies.length > 0 ? (
          <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
            {strategies.map((strategy) => (
              <Card key={strategy.id} className="hover:shadow-lg transition-shadow">
                <CardHeader>
                  <div className="flex items-start justify-between">
                    <div className="space-y-1">
                      <CardTitle className="text-xl">{strategy.name}</CardTitle>
                      <p className="text-sm text-muted-foreground">{strategy.description}</p>
                    </div>
                    <Badge variant={strategy.isActive ? 'success' : 'outline'}>
                      {strategy.isActive ? 'Active' : 'Inactive'}
                    </Badge>
                  </div>
                </CardHeader>
                <CardContent className="space-y-4">
                  {/* Performance Summary */}
                  <div className="space-y-2">
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">Total Trades</span>
                      <span className="font-semibold">{strategy.totalTrades || 0}</span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">Win Rate</span>
                      <span className="font-semibold">{strategy.winRate?.toFixed(1) || 0}%</span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">Total P&L</span>
                      <span
                        className={`font-semibold ${
                          (strategy.totalProfitLoss || 0) >= 0 ? 'text-success' : 'text-destructive'
                        }`}
                      >
                        {formatCurrency(
                          strategy.totalProfitLoss || 0,
                          strategy.displayCurrencySymbol || '$'
                        )}
                      </span>
                    </div>
                  </div>

                  {/* Detailed Stats */}
                  <div className="border-t pt-4 space-y-2">
                    <div className="flex items-center justify-between text-sm">
                      <div className="flex items-center space-x-2">
                        <TrendingUp className="h-4 w-4 text-success" />
                        <span className="text-muted-foreground">Avg Win</span>
                      </div>
                      <span className="font-medium text-success">
                        {formatCurrency(strategy.averageWin || 0, strategy.displayCurrencySymbol || '$')}
                      </span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <div className="flex items-center space-x-2">
                        <TrendingDown className="h-4 w-4 text-destructive" />
                        <span className="text-muted-foreground">Avg Loss</span>
                      </div>
                      <span className="font-medium text-destructive">
                        {formatCurrency(strategy.averageLoss || 0, strategy.displayCurrencySymbol || '$')}
                      </span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <div className="flex items-center space-x-2">
                        <Activity className="h-4 w-4 text-primary" />
                        <span className="text-muted-foreground">Profit Factor</span>
                      </div>
                      <span className="font-medium">{strategy.profitFactor?.toFixed(2) || '0.00'}</span>
                    </div>
                  </div>

                  {/* Actions */}
                  <div className="flex space-x-2 pt-4">
                    <Button
                      variant="outline"
                      className="flex-1"
                      onClick={(e) => {
                        e.stopPropagation();
                        handleEditStrategy(strategy);
                      }}
                    >
                      <Edit className="mr-2 h-4 w-4" />
                      Edit
                    </Button>
                    <Button
                      variant="destructive"
                      className="flex-1"
                      onClick={(e) => {
                        e.stopPropagation();
                        handleDeleteStrategy(strategy);
                      }}
                    >
                      <Trash2 className="mr-2 h-4 w-4" />
                      Delete
                    </Button>
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        ) : (
          <Card>
            <CardContent className="py-12">
              <div className="text-center text-muted-foreground">
                <Target className="mx-auto h-12 w-12 mb-4 opacity-50" />
                <p className="text-lg">No strategies yet</p>
                <p className="text-sm mt-2">Create your first trading strategy to get started</p>
                <Button className="mt-4" onClick={handleCreateStrategy}>
                  <Plus className="mr-2 h-4 w-4" />
                  Add Strategy
                </Button>
              </div>
            </CardContent>
          </Card>
        )}
      </div>
    </AppLayout>
  );
}
