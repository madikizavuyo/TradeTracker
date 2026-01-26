import { useState, useEffect } from 'react';
import { Dialog } from './ui/dialog';
import { Button } from './ui/button';
import { Input } from './ui/input';
import { Label } from './ui/label';
import { Textarea } from './ui/textarea';
import { Strategy } from '@/lib/types';
import { api } from '@/lib/api';

interface StrategyFormModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  strategy?: Strategy | null;
  onSuccess: () => void;
}

export function StrategyFormModal({ open, onOpenChange, strategy, onSuccess }: StrategyFormModalProps) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [isActive, setIsActive] = useState(true);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (strategy) {
      setName(strategy.name);
      setDescription(strategy.description || '');
      setIsActive(strategy.isActive);
    } else {
      setName('');
      setDescription('');
      setIsActive(true);
    }
    setError(null);
  }, [strategy, open]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!name.trim()) {
      setError('Strategy name is required');
      return;
    }

    if (name.length > 100) {
      setError('Strategy name must be less than 100 characters');
      return;
    }

    if (description && description.length > 5000) {
      setError('Description must be less than 5000 characters');
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const strategyData = {
        name: name.trim(),
        description: description.trim() || undefined,
        isActive
      };

      if (strategy) {
        await api.updateStrategy(strategy.id, strategyData);
      } else {
        await api.createStrategy(strategyData);
      }

      onSuccess();
      onOpenChange(false);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to save strategy');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={strategy ? 'Edit Strategy' : 'Create New Strategy'}
      description={strategy ? 'Update your trading strategy details' : 'Add a new trading strategy to track your performance'}
    >
      <form onSubmit={handleSubmit}>
        {error && (
          <div className="mb-4 p-3 rounded-md bg-destructive/10 text-destructive text-sm">
            {error}
          </div>
        )}

        <div className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="name">
              Strategy Name <span className="text-destructive">*</span>
            </Label>
            <Input
              id="name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g., Trend Following"
              maxLength={100}
              required
            />
            <p className="text-xs text-muted-foreground">{name.length}/100 characters</p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="description">Description</Label>
            <Textarea
              id="description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Describe your trading strategy..."
              rows={4}
              maxLength={5000}
            />
            <p className="text-xs text-muted-foreground">{description.length}/5000 characters</p>
          </div>

          <div className="flex items-center space-x-2">
            <input
              id="isActive"
              type="checkbox"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
              className="h-4 w-4 rounded border-input"
            />
            <label htmlFor="isActive" className="text-sm cursor-pointer">
              Active (strategy is currently being used)
            </label>
          </div>
        </div>

        <div className="flex justify-end space-x-2 mt-6">
          <Button
            type="button"
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={loading}
          >
            Cancel
          </Button>
          <Button type="submit" disabled={loading}>
            {loading ? 'Saving...' : strategy ? 'Update' : 'Create'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

