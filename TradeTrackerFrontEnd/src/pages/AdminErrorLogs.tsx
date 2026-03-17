import { useEffect, useState } from 'react';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { AlertTriangle, ChevronLeft, ChevronRight, FileWarning, Loader2 } from 'lucide-react';
import { api } from '@/lib/api';

interface ErrorLogItem {
  id: number;
  timestamp: string;
  level: string;
  category: string;
  message: string;
  exception?: string;
}

export default function AdminErrorLogs() {
  const [items, setItems] = useState<ErrorLogItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(50);
  const [totalPages, setTotalPages] = useState(0);
  const [loading, setLoading] = useState(true);
  const [levelFilter, setLevelFilter] = useState<string>('');
  const [error, setError] = useState<string | null>(null);

  const loadLogs = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getAdminErrorLogs(page, pageSize, levelFilter || undefined);
      setItems(res.items);
      setTotalCount(res.totalCount);
      setTotalPages(res.totalPages);
    } catch (err: any) {
      setError(err?.response?.data?.message || err?.message || 'Failed to load error logs');
      setItems([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadLogs();
  }, [page, levelFilter]);

  const formatTimestamp = (iso: string) => {
    try {
      return new Date(iso).toLocaleString(undefined, {
        dateStyle: 'medium',
        timeStyle: 'medium',
      });
    } catch {
      return iso;
    }
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        <div className="flex items-center space-x-3">
          <FileWarning className="h-8 w-8 text-primary" />
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-primary">Error Logs</h1>
            <p className="text-muted-foreground">
              Application errors and warnings from the last 5 days. Use for investigation.
            </p>
          </div>
        </div>

        {error && (
          <div className="flex items-center space-x-2 p-4 rounded-lg bg-destructive/10 text-destructive border border-destructive/20">
            <AlertTriangle className="h-5 w-5 shrink-0" />
            <span className="font-medium">{error}</span>
          </div>
        )}

        <Card>
          <CardHeader>
            <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
              <div>
                <CardTitle>Application Logs</CardTitle>
                <CardDescription>
                  Errors and warnings only. Logs older than 5 days are automatically removed.
                </CardDescription>
              </div>
              <div className="flex items-center gap-2">
                <select
                  value={levelFilter}
                  onChange={(e) => {
                    setLevelFilter(e.target.value);
                    setPage(1);
                  }}
                  className="h-9 rounded-md border border-input bg-background px-3 py-1 text-sm"
                >
                  <option value="">All (Error + Warning)</option>
                  <option value="Error">Error only</option>
                  <option value="Warning">Warning only</option>
                </select>
              </div>
            </div>
          </CardHeader>
          <CardContent>
            {loading ? (
              <div className="flex items-center justify-center py-12">
                <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
              </div>
            ) : items.length === 0 ? (
              <div className="py-12 text-center text-muted-foreground">
                No error logs found for the last 5 days.
              </div>
            ) : (
              <div className="space-y-4">
                <div className="text-sm text-muted-foreground">
                  Showing {items.length} of {totalCount} entries
                </div>
                <div className="space-y-3">
                  {items.map((log) => (
                    <div
                      key={log.id}
                      className="rounded-lg border border-border bg-muted/20 p-4 space-y-2"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge
                          variant={log.level === 'Error' ? 'destructive' : 'secondary'}
                          className="text-xs"
                        >
                          {log.level}
                        </Badge>
                        <span className="text-xs text-muted-foreground font-mono">
                          {formatTimestamp(log.timestamp)}
                        </span>
                        <span className="text-xs text-muted-foreground truncate max-w-[200px]">
                          {log.category}
                        </span>
                      </div>
                      <p className="text-sm font-medium break-words">{log.message}</p>
                      {log.exception && (
                        <pre className="text-xs text-muted-foreground overflow-x-auto whitespace-pre-wrap break-words bg-muted/50 p-2 rounded">
                          {log.exception}
                        </pre>
                      )}
                    </div>
                  ))}
                </div>

                {totalPages > 1 && (
                  <div className="flex items-center justify-between pt-4 border-t">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setPage((p) => Math.max(1, p - 1))}
                      disabled={page <= 1}
                    >
                      <ChevronLeft className="h-4 w-4 mr-1" />
                      Previous
                    </Button>
                    <span className="text-sm text-muted-foreground">
                      Page {page} of {totalPages}
                    </span>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                      disabled={page >= totalPages}
                    >
                      Next
                      <ChevronRight className="h-4 w-4 ml-1" />
                    </Button>
                  </div>
                )}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </AppLayout>
  );
}
