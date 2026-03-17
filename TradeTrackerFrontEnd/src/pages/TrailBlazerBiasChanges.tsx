import { useCallback, useEffect, useState } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { api } from '@/lib/api';
import { TrailBlazerBiasChange } from '@/lib/types';
import { TrendingUp, TrendingDown, Minus, Search } from 'lucide-react';
import { Input } from '@/components/ui/input';

export default function TrailBlazerBiasChanges() {
  const [changes, setChanges] = useState<TrailBlazerBiasChange[]>([]);
  const [loading, setLoading] = useState(true);
  const [lastHours, setLastHours] = useState(48);
  const [searchQuery, setSearchQuery] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await api.getTrailBlazerBiasChanges(lastHours, 30);
      setChanges(data);
    } catch {
      setChanges([]);
    } finally {
      setLoading(false);
    }
  }, [lastHours]);

  useEffect(() => {
    load();
  }, [load]);

  const getBiasIcon = (bias: string) => {
    if (bias === 'Bullish') return <TrendingUp className="h-4 w-4 text-green-600" />;
    if (bias === 'Bearish') return <TrendingDown className="h-4 w-4 text-red-600" />;
    return <Minus className="h-4 w-4 text-muted-foreground" />;
  };

  const getBiasVariant = (bias: string) => {
    if (bias === 'Bullish') return 'default' as const;
    if (bias === 'Bearish') return 'destructive' as const;
    return 'outline' as const;
  };

  const formatDate = (s: string) => {
    const d = new Date(s);
    return d.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
  };

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>Bias Changes</CardTitle>
          <p className="text-sm text-muted-foreground">
            Instruments that changed Bias in the last{' '}
            <select
              value={lastHours}
              onChange={(e) => setLastHours(Number(e.target.value))}
              className="rounded border bg-background px-2 py-1 text-sm"
            >
              <option value={24}>24 hours</option>
              <option value={48}>48 hours</option>
              <option value={72}>72 hours</option>
              <option value={168}>7 days</option>
            </select>
          </p>
          <div className="relative mt-2 w-48">
            <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Search instruments..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="pl-8 h-8 text-sm"
            />
          </div>
        </CardHeader>
        <CardContent>
          {loading ? (
            <p className="text-muted-foreground">Loading...</p>
          ) : changes.length === 0 ? (
            <p className="text-muted-foreground">No bias changes in this period.</p>
          ) : (
            <div className="space-y-3">
              {changes
                .filter(c => !searchQuery.trim() || c.instrumentName.toLowerCase().includes(searchQuery.trim().toLowerCase()))
                .map((c) => (
                <div
                  key={`${c.instrumentId}-${c.changedAt}`}
                  className="flex items-center justify-between rounded-lg border p-3"
                >
                  <div className="flex items-center gap-3">
                    <span className="font-medium">{c.instrumentName}</span>
                    <Badge variant={getBiasVariant(c.previousBias)} className="gap-1">
                      {getBiasIcon(c.previousBias)}
                      {c.previousBias}
                    </Badge>
                    <span className="text-muted-foreground">→</span>
                    <Badge variant={getBiasVariant(c.newBias)} className="gap-1">
                      {getBiasIcon(c.newBias)}
                      {c.newBias}
                    </Badge>
                  </div>
                  <div className="flex items-center gap-4 text-sm text-muted-foreground">
                    <span>Score: {c.overallScore.toFixed(1)}</span>
                    <span>{formatDate(c.changedAt)}</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
