import { useEffect, useState, useCallback } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { EconomicHeatmap } from '@/components/EconomicHeatmap';
import { COTChart } from '@/components/COTChart';
import { StatusDot } from '@/components/StatusDot';
import { RefreshCw, TrendingUp, TrendingDown, ChevronLeft, ChevronRight, Sparkles, Newspaper, ExternalLink, Search } from 'lucide-react';
import { api } from '@/lib/api';
import { useTrailBlazerRefresh } from '@/contexts/TrailBlazerRefreshContext';
import {
  TrailBlazerScore,
  TrailBlazerTopSetups,
  HeatmapEntry,
  COTData,
  SentimentData,
  TrailBlazerNewsItem,
  TrailBlazerOutlookItem,
} from '@/lib/types';

export default function TrailBlazer() {
  const location = useLocation();
  const navigate = useNavigate();
  const { triggerRefresh, setTabStatus } = useTrailBlazerRefresh();
  const [topSetups, setTopSetups] = useState<TrailBlazerTopSetups>({ bullish: [], bearish: [] });
  const [heatmap, setHeatmap] = useState<HeatmapEntry[]>([]);
  const [cotData, setCotData] = useState<COTData[]>([]);
  const [sentiment, setSentiment] = useState<SentimentData[]>([]);
  const [selectedInstrument, setSelectedInstrument] = useState<TrailBlazerScore | null>(null);
  const [news, setNews] = useState<TrailBlazerNewsItem[]>([]);
  const [newsLoading, setNewsLoading] = useState(false);
  const [outlook, setOutlook] = useState<TrailBlazerOutlookItem[]>([]);
  const [outlookLoading, setOutlookLoading] = useState(false);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [scrapingCOT, setScrapingCOT] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sentimentPage, setSentimentPage] = useState(0);
  const [aiAnalysis, setAiAnalysis] = useState<string | null>(null);
  const [aiAnalysisLoading, setAiAnalysisLoading] = useState(false);
  const [aiAnalysisError, setAiAnalysisError] = useState<string | null>(null);
  const PAGE_SIZE = 10;

  const loadData = useCallback(async (backgroundRefresh = false) => {
    try {
      if (!backgroundRefresh) setLoading(true);
      const [setupsRes, heatmapRes, cotRes, sentimentRes] = await Promise.all([
        api.getTrailBlazerTopSetups(),
        api.getTrailBlazerHeatmap(),
        api.getTrailBlazerCOT(),
        api.getTrailBlazerSentiment(),
      ]);
      setTopSetups(setupsRes);
      setHeatmap(heatmapRes);
      setCotData(cotRes);
      setSentiment(sentimentRes);
      setError(null);
    } catch (err) {
      console.error('Failed to load TrailBlazer data:', err);
      setError('Failed to load TrailBlazer data. The data may not have been generated yet.');
    } finally {
      setLoading(false);
    }
  }, []);

  const loadAiAnalysis = useCallback(async (instrumentId: number) => {
    setAiAnalysisLoading(true);
    setAiAnalysisError(null);
    setAiAnalysis(null);
    try {
      const { analysis } = await api.getTrailBlazerAnalysis(instrumentId);
      setAiAnalysis(analysis);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: Record<string, unknown> } };
      const data = ax?.response?.data;
      const msg = data && (typeof data.message === 'string' ? data.message : typeof data.detail === 'string' ? data.detail : typeof data.title === 'string' ? data.title : null);
      console.error('AI analysis failed:', data ?? err);
      setAiAnalysisError(msg || 'Could not generate AI analysis. Ensure the Google API key is configured.');
    } finally {
      setAiAnalysisLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, []);

  useEffect(() => {
    setTabStatus('overview', loading ? 'loading' : error ? 'error' : 'idle');
  }, [loading, error, setTabStatus]);

  useEffect(() => {
    const onReload = () => loadData(true);
    window.addEventListener('trailblazer-reload-now', onReload);
    return () => window.removeEventListener('trailblazer-reload-now', onReload);
  }, [loadData]);

  useEffect(() => {
    const fromState = (location.state as { selectedInstrument?: TrailBlazerScore })?.selectedInstrument;
    if (fromState) {
      setSelectedInstrument(fromState);
      navigate(location.pathname, { replace: true, state: {} });
    }
  }, [location.state, navigate, location.pathname]);

  const loadNews = useCallback(async (symbol: string) => {
    setNewsLoading(true);
    setNews([]);
    try {
      const res = await api.getTrailBlazerNews(symbol);
      setNews(res);
    } catch {
      setNews([]);
    } finally {
      setNewsLoading(false);
    }
  }, []);

  useEffect(() => {
    if (selectedInstrument) {
      loadAiAnalysis(selectedInstrument.instrumentId);
      loadNews(selectedInstrument.instrumentName);
    } else {
      setAiAnalysis(null);
      setAiAnalysisError(null);
      setNews([]);
    }
  }, [selectedInstrument, loadAiAnalysis, loadNews]);

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      await triggerRefresh();
    } catch (err) {
      console.error('Refresh failed:', err);
    } finally {
      setRefreshing(false);
    }
  };

  const handleScrapeCOT = async () => {
    setScrapingCOT(true);
    try {
      await api.scrapeTrailBlazerCOT();
      await loadData();
    } catch (err) {
      console.error('Scrape COT failed:', err);
    } finally {
      setScrapingCOT(false);
    }
  };

  const sentimentTotalPages = Math.ceil(sentiment.length / PAGE_SIZE);
  const paginatedSentiment = sentiment.slice(sentimentPage * PAGE_SIZE, (sentimentPage + 1) * PAGE_SIZE);

  if (loading) {
    return (
      <div className="flex flex-col items-center justify-center h-96 gap-4">
        <div className="flex items-center gap-2">
          <StatusDot status="loading" />
          <span className="text-muted-foreground">Loading TrailBlazer data...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <StatusDot status={error ? 'error' : 'idle'} title={error ?? 'Data loaded'} />
          <span>{error ? 'Load failed' : 'Overview'}</span>
        </div>
        {error && (
          <Card>
            <CardContent className="py-8 text-center">
              <p className="text-muted-foreground mb-4">{error}</p>
              <Button onClick={handleRefresh} disabled={refreshing}>
                <RefreshCw className={`h-4 w-4 mr-2 ${refreshing ? 'animate-spin' : ''}`} />
                Generate Data
              </Button>
            </CardContent>
          </Card>
        )}

        {/* Top Setups Scanner */}
        {(topSetups.bullish.length > 0 || topSetups.bearish.length > 0) && (
          <div className="grid gap-4 md:grid-cols-2">
            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="flex items-center gap-2 text-base">
                  <StatusDot status={error ? 'error' : 'idle'} />
                  <TrendingUp className="h-5 w-5 text-green-500" />
                  Top Bullish Setups
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-3">
                {topSetups.bullish.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No bullish setups found</p>
                ) : topSetups.bullish.map((setup) => (
                  <div
                    key={setup.instrumentId}
                    className="flex items-center justify-between p-3 rounded-lg bg-green-500/5 border border-green-500/20 cursor-pointer hover:bg-green-500/10 transition-colors"
                    onClick={() => setSelectedInstrument(setup)}
                  >
                    <div>
                      <span className="font-semibold">{setup.instrumentName}</span>
                      <span className="text-xs text-muted-foreground ml-2">{setup.assetClass}</span>
                    </div>
                    <div className="flex items-center gap-2">
                      <Badge variant="success">{setup.overallScore.toFixed(1)}</Badge>
                    </div>
                  </div>
                ))}
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="flex items-center gap-2 text-base">
                  <StatusDot status={error ? 'error' : 'idle'} />
                  <TrendingDown className="h-5 w-5 text-red-500" />
                  Top Bearish Setups
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-3">
                {topSetups.bearish.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No bearish setups found</p>
                ) : topSetups.bearish.map((setup) => (
                  <div
                    key={setup.instrumentId}
                    className="flex items-center justify-between p-3 rounded-lg bg-red-500/5 border border-red-500/20 cursor-pointer hover:bg-red-500/10 transition-colors"
                    onClick={() => setSelectedInstrument(setup)}
                  >
                    <div>
                      <span className="font-semibold">{setup.instrumentName}</span>
                      <span className="text-xs text-muted-foreground ml-2">{setup.assetClass}</span>
                    </div>
                    <div className="flex items-center gap-2">
                      <Badge variant="destructive">{setup.overallScore.toFixed(1)}</Badge>
                    </div>
                  </div>
                ))}
              </CardContent>
            </Card>
          </div>
        )}

      <p className="text-sm text-muted-foreground">
        View the <Link to="/trailblazer/scanner" className="text-primary hover:underline">Asset Scanner</Link> for the full table, <Link to="/trailblazer/strength" className="text-primary hover:underline">Strength</Link> for rankings, or <Link to="/trailblazer/news-sentiment" className="text-primary hover:underline">News Sentiment</Link> for headline-based sentiment.
      </p>

        {/* Selected Instrument Detail: AI Analysis + News + Outlook (Score History & Breakdown on Asset Scanner) */}
        {selectedInstrument && (
          <div className="space-y-4">
            <p className="text-sm text-muted-foreground">
              <Link to="/trailblazer/scanner" state={{ selectedInstrument }} className="text-primary hover:underline">View score breakdown and history on Asset Scanner</Link>
            </p>
            {/* AI Analysis + News + Outlook */}
            <div className="grid gap-4 md:grid-cols-3">
            <Card className="border-primary/30 bg-primary/5">
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <StatusDot status={aiAnalysisLoading ? 'loading' : aiAnalysisError ? 'error' : 'idle'} />
                  <Sparkles className="h-5 w-5 text-primary" />
                  AI Analysis
                </CardTitle>
                <p className="text-sm text-muted-foreground">
                  Opinionated analysis based on the score, fundamentals, COT positioning, and retail sentiment.
                </p>
              </CardHeader>
              <CardContent>
                {aiAnalysisLoading ? (
                  <div className="flex items-center gap-2 text-muted-foreground py-8">
                    <RefreshCw className="h-4 w-4 animate-spin" />
                    <span>Generating AI analysis...</span>
                  </div>
                ) : aiAnalysisError ? (
                  <p className="text-destructive py-4">{aiAnalysisError}</p>
                ) : aiAnalysis ? (
                  <div className="prose prose-sm dark:prose-invert max-w-none">
                    <div className="whitespace-pre-wrap text-foreground leading-relaxed">{aiAnalysis}</div>
                  </div>
                ) : null}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <StatusDot status={newsLoading ? 'loading' : 'idle'} />
                  <Newspaper className="h-5 w-5" />
                  News
                </CardTitle>
                <p className="text-sm text-muted-foreground">
                  Market news for {selectedInstrument.instrumentName} (Brave + Finnhub).
                </p>
              </CardHeader>
              <CardContent>
                {newsLoading ? (
                  <div className="flex items-center gap-2 text-muted-foreground py-8">
                    <RefreshCw className="h-4 w-4 animate-spin" />
                    <span>Loading news...</span>
                  </div>
                ) : news.length === 0 ? (
                  <p className="text-muted-foreground py-4">No news available.</p>
                ) : (
                  <div className="space-y-4">
                    {news.slice(0, 5).map((item, i) => (
                      <a
                        key={i}
                        href={item.url}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="block p-3 rounded-lg border border-border hover:bg-accent/50 transition-colors"
                      >
                        <div className="flex items-start justify-between gap-2">
                          <h4 className="font-medium text-sm line-clamp-2">{item.headline}</h4>
                          <ExternalLink className="h-4 w-4 shrink-0 text-muted-foreground" />
                        </div>
                        {item.summary && (
                          <p className="text-xs text-muted-foreground mt-1 line-clamp-2">{item.summary}</p>
                        )}
                        <div className="flex items-center gap-2 mt-2 text-xs text-muted-foreground">
                          <span>{item.source}</span>
                          <span>•</span>
                          <span>{item.publishedAt ? new Date(item.publishedAt).toLocaleString() : ''}</span>
                        </div>
                      </a>
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <StatusDot status={outlookLoading ? 'loading' : 'idle'} />
                  <Search className="h-5 w-5" />
                  Market Outlook
                </CardTitle>
                <p className="text-sm text-muted-foreground">
                  Forecast and outlook snippets for {selectedInstrument.instrumentName} from Brave web search.
                </p>
              </CardHeader>
              <CardContent>
                {outlookLoading ? (
                  <div className="flex items-center gap-2 text-muted-foreground py-8">
                    <RefreshCw className="h-4 w-4 animate-spin" />
                    <span>Loading outlook...</span>
                  </div>
                ) : outlook.length === 0 ? (
                  <p className="text-muted-foreground py-4">No outlook snippets available.</p>
                ) : (
                  <div className="space-y-4">
                    {outlook.slice(0, 5).map((item, i) => (
                      <a
                        key={i}
                        href={item.url}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="block p-3 rounded-lg border border-border hover:bg-accent/50 transition-colors"
                      >
                        <div className="flex items-start justify-between gap-2">
                          <h4 className="font-medium text-sm line-clamp-2">{item.title}</h4>
                          <ExternalLink className="h-4 w-4 shrink-0 text-muted-foreground" />
                        </div>
                        {item.description && (
                          <p className="text-xs text-muted-foreground mt-1 line-clamp-2">{item.description}</p>
                        )}
                        {item.source && (
                          <span className="text-xs text-muted-foreground mt-2 block">{item.source}</span>
                        )}
                      </a>
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
            </div>
          </div>
        )}

        {/* Economic Heatmap */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <StatusDot status={error ? 'error' : 'idle'} />
              Economic Heatmap
            </CardTitle>
          </CardHeader>
          <CardContent>
            <EconomicHeatmap data={heatmap} />
          </CardContent>
        </Card>

        {/* COT Positioning */}
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <CardTitle className="flex items-center gap-2">
              <StatusDot status={error ? 'error' : scrapingCOT ? 'loading' : 'idle'} />
              COT Institutional Positioning
            </CardTitle>
            <div className="flex gap-2">
              <Button size="sm" variant="outline" onClick={() => loadData()} disabled={loading}>
                {loading ? 'Loading...' : 'Reload'}
              </Button>
              <Button size="sm" variant="outline" onClick={handleScrapeCOT} disabled={scrapingCOT}>
                {scrapingCOT ? 'Scraping...' : 'Scrape COT'}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <COTChart data={cotData} />
          </CardContent>
        </Card>

        {/* Retail Sentiment */}
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <CardTitle className="flex items-center gap-2">
              <StatusDot status={error ? 'error' : 'idle'} />
              Retail Sentiment
            </CardTitle>
            <Button size="sm" variant="outline" onClick={() => loadData()} disabled={loading}>
              {loading ? 'Loading...' : 'Reload'}
            </Button>
          </CardHeader>
          <CardContent>
            {sentiment.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-40 text-muted-foreground gap-3">
                <span>No sentiment data yet. Run a TrailBlazer refresh to populate from MyFXBook.</span>
              </div>
            ) : (
              <div>
                <div className="space-y-3">
                  {paginatedSentiment.map(s => (
                    <div key={s.symbol} className="flex items-center gap-3">
                      <span className="w-20 text-sm font-medium shrink-0">{s.symbol}</span>
                      <div className="flex-1 flex h-6 rounded-full overflow-hidden">
                        <div
                          className="bg-green-500 flex items-center justify-center text-xs font-medium text-white"
                          style={{ width: `${s.longPct}%` }}
                        >
                          {s.longPct.toFixed(0)}%
                        </div>
                        <div
                          className="bg-red-500 flex items-center justify-center text-xs font-medium text-white"
                          style={{ width: `${s.shortPct}%` }}
                        >
                          {s.shortPct.toFixed(0)}%
                        </div>
                      </div>
                      <span className="text-xs text-muted-foreground w-12 text-right shrink-0">
                        {s.longPct > s.shortPct ? 'Long' : 'Short'}
                      </span>
                    </div>
                  ))}
                </div>
                {sentimentTotalPages > 1 && (
                  <div className="flex items-center justify-between mt-4 pt-3 border-t border-border">
                    <span className="text-xs text-muted-foreground">
                      Showing {sentimentPage * PAGE_SIZE + 1}–{Math.min((sentimentPage + 1) * PAGE_SIZE, sentiment.length)} of {sentiment.length}
                    </span>
                    <div className="flex items-center gap-1">
                      <Button size="sm" variant="outline" className="h-7 w-7 p-0" disabled={sentimentPage === 0} onClick={() => setSentimentPage(p => p - 1)}>
                        <ChevronLeft className="h-4 w-4" />
                      </Button>
                      {Array.from({ length: sentimentTotalPages }, (_, i) => (
                        <Button
                          key={i}
                          size="sm"
                          variant={sentimentPage === i ? 'default' : 'outline'}
                          className="h-7 w-7 p-0 text-xs"
                          onClick={() => setSentimentPage(i)}
                        >
                          {i + 1}
                        </Button>
                      ))}
                      <Button size="sm" variant="outline" className="h-7 w-7 p-0" disabled={sentimentPage >= sentimentTotalPages - 1} onClick={() => setSentimentPage(p => p + 1)}>
                        <ChevronRight className="h-4 w-4" />
                      </Button>
                    </div>
                  </div>
                )}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
  );
}
