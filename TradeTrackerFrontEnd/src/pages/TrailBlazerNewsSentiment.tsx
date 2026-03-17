import React, { useCallback, useEffect, useState } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { StatusDot } from '@/components/StatusDot';
import { Newspaper, ExternalLink, ChevronDown, ChevronRight, Search } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { api } from '@/lib/api';
import { useTrailBlazerRefresh } from '@/contexts/TrailBlazerRefreshContext';
import { TrailBlazerScore, TrailBlazerNewsItem } from '@/lib/types';

export default function TrailBlazerNewsSentiment() {
  const { setTabStatus } = useTrailBlazerRefresh();
  const [scores, setScores] = useState<TrailBlazerScore[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [articles, setArticles] = useState<TrailBlazerNewsItem[]>([]);
  const [articlesLoading, setArticlesLoading] = useState(false);

  const load = useCallback(async (backgroundRefresh = false) => {
    try {
      if (!backgroundRefresh) setLoading(true);
      const res = await api.getTrailBlazerScores();
      const sorted = [...res].sort((a, b) => (b.newsSentimentScore ?? 5) - (a.newsSentimentScore ?? 5));
      setScores(sorted);
      setError(null);
    } catch (err) {
      console.error('Failed to load TrailBlazer scores:', err);
      setError('Failed to load TrailBlazer data.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    setTabStatus('news-sentiment', loading ? 'loading' : error ? 'error' : 'idle');
  }, [loading, error, setTabStatus]);

  useEffect(() => {
    const onReload = () => load(true);
    window.addEventListener('trailblazer-reload-now', onReload);
    return () => window.removeEventListener('trailblazer-reload-now', onReload);
  }, [load]);

  const fetchArticles = useCallback(async (symbol: string) => {
    setArticlesLoading(true);
    setArticles([]);
    try {
      const res = await api.getTrailBlazerNews(symbol);
      setArticles(Array.isArray(res) ? res.slice(0, 5) : []);
    } catch {
      setArticles([]);
    } finally {
      setArticlesLoading(false);
    }
  }, []);

  const handleRowClick = useCallback((s: TrailBlazerScore) => {
    if (expandedId === s.id) {
      setExpandedId(null);
      setArticles([]);
      return;
    }
    setExpandedId(s.id);
    fetchArticles(s.instrumentName);
  }, [expandedId, fetchArticles]);

  const getNewsSentimentLabel = (score: number) => {
    if (score >= 6.5) return 'Bullish';
    if (score <= 3.5) return 'Bearish';
    return 'Neutral';
  };

  const getSentimentVariant = (score: number) => {
    if (score >= 6.5) return 'success' as const;
    if (score <= 3.5) return 'destructive' as const;
    return 'outline' as const;
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-96 gap-2">
        <StatusDot status="loading" />
        <span className="text-muted-foreground">Loading News Sentiment...</span>
      </div>
    );
  }

  if (error) {
    return (
      <Card>
        <CardContent className="py-8 text-center">
          <div className="flex items-center justify-center gap-2 mb-2">
            <StatusDot status="error" />
            <span className="text-muted-foreground">{error}</span>
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <StatusDot status="idle" />
          <Newspaper className="h-5 w-5" />
          News Sentiment
        </CardTitle>
        <p className="text-sm text-muted-foreground">
          Instruments ranked by news sentiment score (1–10). Derived from headline analysis via Brave/Finnhub. Higher = more bullish news tone.
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
        {scores.length === 0 ? (
          <p className="text-center text-muted-foreground py-8">No scores available yet. Run a TrailBlazer refresh to populate news sentiment.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border">
                  <th className="p-2 w-8" aria-label="Expand" />
                  <th className="text-left p-2 w-12">#</th>
                  <th className="text-left p-2">Instrument</th>
                  <th className="text-center p-2">News Sentiment</th>
                  <th className="text-center p-2">Overall Score</th>
                  <th className="text-center p-2">News Tone</th>
                  <th className="text-center p-2">Asset Class</th>
                </tr>
              </thead>
              <tbody>
                {scores
                  .filter(s => !searchQuery.trim() || s.instrumentName.toLowerCase().includes(searchQuery.trim().toLowerCase()))
                  .map((s, idx) => (
                  <React.Fragment key={s.id}>
                    <tr
                      className="border-b border-border/50 hover:bg-accent/50 cursor-pointer transition-colors"
                      onClick={() => handleRowClick(s)}
                    >
                      <td className="p-2 w-8 text-muted-foreground">
                        {expandedId === s.id ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                      </td>
                      <td className="p-2 font-medium text-muted-foreground">{idx + 1}</td>
                      <td className="p-2 font-medium">{s.instrumentName}</td>
                      <td className="p-2 text-center">
                        <span className={`font-bold ${(s.newsSentimentScore ?? 5) >= 6.5 ? 'text-green-500' : (s.newsSentimentScore ?? 5) <= 3.5 ? 'text-red-500' : 'text-yellow-500'}`}>
                          {(s.newsSentimentScore ?? 5).toFixed(1)}
                        </span>
                      </td>
                      <td className="p-2 text-center">
                        <span className="text-muted-foreground">{s.overallScore.toFixed(1)}</span>
                      </td>
                      <td className="p-2 text-center">
                        <Badge variant={getSentimentVariant(s.newsSentimentScore ?? 5)}>{getNewsSentimentLabel(s.newsSentimentScore ?? 5)}</Badge>
                      </td>
                      <td className="p-2 text-center">
                        <span className="text-xs text-muted-foreground">{s.assetClass}</span>
                      </td>
                    </tr>
                    {expandedId === s.id && (
                      <tr key={`${s.id}-articles`}>
                        <td colSpan={7} className="p-3 bg-muted/30 border-b border-border/50">
                          <p className="text-xs font-medium text-muted-foreground mb-2">Articles that influenced the score (up to 5)</p>
                          {articlesLoading ? (
                            <span className="text-sm text-muted-foreground">Loading…</span>
                          ) : articles.length === 0 ? (
                            <span className="text-sm text-muted-foreground">No recent articles in DB. Run a refresh to fetch.</span>
                          ) : (
                            <ul className="space-y-1.5 text-sm">
                              {articles.map((a, i) => (
                                <li key={i}>
                                  <a href={a.url} target="_blank" rel="noopener noreferrer" className="text-primary hover:underline inline-flex items-center gap-1">
                                    {a.headline}
                                    <ExternalLink className="h-3 w-3 shrink-0" />
                                  </a>
                                  {a.source && <span className="text-muted-foreground ml-1">({a.source})</span>}
                                </li>
                              ))}
                            </ul>
                          )}
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
