import { useEffect, useState } from 'react';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';
import { Badge } from '@/components/ui/badge';
import { Brain, TrendingUp, TrendingDown, AlertTriangle, CheckCircle2, Target, Calendar } from 'lucide-react';
import { api } from '@/lib/api';
import { Strategy } from '@/lib/types';

interface AIInsightsData {
  overallPerformance: string;
  strengths: string[];
  weaknesses: string[];
  recommendations: string[];
  bestInstruments: string[];
  worstInstruments: string[];
  optimalTimeframes: string[];
  emotionalPatterns: string;
  nextSteps: string[];
  confidence: number;
}

export default function AIInsights() {
  const [insights, setInsights] = useState<AIInsightsData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [strategies, setStrategies] = useState<Strategy[]>([]);
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [selectedStrategy, setSelectedStrategy] = useState<string>('all');

  useEffect(() => {
    loadStrategies();
  }, []);

  const loadStrategies = async () => {
    try {
      const response = await api.getStrategies();
      setStrategies(response.data || response || []);
    } catch (error) {
      console.error('Failed to load strategies:', error);
    }
  };

  const loadInsights = async () => {
    setLoading(true);
    setError(null);

    try {
      const strategyId = selectedStrategy === 'all' ? undefined : parseInt(selectedStrategy);
      const response = await api.getAIInsights(
        startDate || undefined,
        endDate || undefined,
        strategyId
      );

      const data = response.data || response;
      setInsights(data);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to load AI insights');
    } finally {
      setLoading(false);
    }
  };

  const handleGenerateInsights = () => {
    loadInsights();
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center space-x-3">
          <Brain className="h-8 w-8 text-primary" />
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-primary">AI Trading Insights</h1>
            <p className="text-muted-foreground">
              Get AI-powered analysis of your trading performance
            </p>
          </div>
        </div>

        {/* Filters */}
        <Card>
          <CardHeader>
            <CardTitle>Analysis Parameters</CardTitle>
            <CardDescription>Configure the scope of AI analysis</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 md:grid-cols-3">
              <div className="space-y-2">
                <Label htmlFor="startDate">Start Date</Label>
                <Input
                  id="startDate"
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="endDate">End Date</Label>
                <Input
                  id="endDate"
                  type="date"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="strategy">Strategy</Label>
                <Select
                  id="strategy"
                  value={selectedStrategy}
                  onChange={(e) => setSelectedStrategy(e.target.value)}
                >
                  <option value="all">All Strategies</option>
                  {strategies.map((strategy) => (
                    <option key={strategy.id} value={strategy.id}>
                      {strategy.name}
                    </option>
                  ))}
                </Select>
              </div>
            </div>
            <Button onClick={handleGenerateInsights} disabled={loading} className="w-full">
              {loading ? 'Analyzing...' : 'Generate AI Insights'}
            </Button>
          </CardContent>
        </Card>

        {/* Error */}
        {error && (
          <Card className="border-destructive">
            <CardContent className="pt-6">
              <div className="flex items-center space-x-2 text-destructive">
                <AlertTriangle className="h-5 w-5" />
                <p>{error}</p>
              </div>
            </CardContent>
          </Card>
        )}

        {/* Insights Display */}
        {insights && !loading && (
          <>
            {/* Overall Performance */}
            <Card>
              <CardHeader>
                <CardTitle>Overall Performance</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-muted-foreground">{insights.overallPerformance}</p>
                <div className="mt-4 flex items-center space-x-2">
                  <span className="text-sm text-muted-foreground">Confidence:</span>
                  <div className="flex-1 h-2 bg-muted rounded-full overflow-hidden">
                    <div
                      className="h-full bg-primary transition-all"
                      style={{ width: `${insights.confidence * 100}%` }}
                    />
                  </div>
                  <span className="text-sm font-medium">
                    {(insights.confidence * 100).toFixed(0)}%
                  </span>
                </div>
              </CardContent>
            </Card>

            <div className="grid gap-6 md:grid-cols-2">
              {/* Strengths */}
              <Card className="border-success/20 bg-success/5">
                <CardHeader>
                  <CardTitle className="flex items-center space-x-2">
                    <CheckCircle2 className="h-5 w-5 text-success" />
                    <span>Strengths</span>
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  <ul className="space-y-2">
                    {insights.strengths.map((strength, index) => (
                      <li key={index} className="flex items-start space-x-2 text-sm">
                        <span className="text-success mt-1">•</span>
                        <span>{strength}</span>
                      </li>
                    ))}
                  </ul>
                </CardContent>
              </Card>

              {/* Weaknesses */}
              <Card className="border-destructive/20 bg-destructive/5">
                <CardHeader>
                  <CardTitle className="flex items-center space-x-2">
                    <AlertTriangle className="h-5 w-5 text-destructive" />
                    <span>Weaknesses</span>
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  <ul className="space-y-2">
                    {insights.weaknesses.map((weakness, index) => (
                      <li key={index} className="flex items-start space-x-2 text-sm">
                        <span className="text-destructive mt-1">•</span>
                        <span>{weakness}</span>
                      </li>
                    ))}
                  </ul>
                </CardContent>
              </Card>
            </div>

            {/* Recommendations */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center space-x-2">
                  <Target className="h-5 w-5 text-primary" />
                  <span>Recommendations</span>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="space-y-3">
                    {insights.recommendations.map((recommendation, index) => (
                      <li key={index} className="flex items-start space-x-2">
                        <span className="font-semibold text-primary mt-1">{index + 1}.</span>
                        <span className="text-muted-foreground">{recommendation}</span>
                      </li>
                    ))}
                  </ul>
                </CardContent>
              </Card>

              {/* Instrument Performance */}
              {insights.bestInstruments.length > 0 && (
                <Card>
                  <CardHeader>
                    <CardTitle>Instrument Performance</CardTitle>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div>
                      <h4 className="font-medium mb-2 flex items-center space-x-2">
                        <TrendingUp className="h-4 w-4 text-success" />
                        <span>Best Performers</span>
                      </h4>
                      <div className="flex flex-wrap gap-2">
                        {insights.bestInstruments.map((instrument, index) => (
                          <Badge key={index} variant="success">
                            {instrument}
                          </Badge>
                        ))}
                      </div>
                    </div>

                    {insights.worstInstruments.length > 0 && (
                      <div>
                        <h4 className="font-medium mb-2 flex items-center space-x-2">
                          <TrendingDown className="h-4 w-4 text-destructive" />
                          <span>Underperformers</span>
                        </h4>
                        <div className="flex flex-wrap gap-2">
                          {insights.worstInstruments.map((instrument, index) => (
                            <Badge key={index} variant="destructive">
                              {instrument}
                            </Badge>
                          ))}
                        </div>
                      </div>
                    )}
                  </CardContent>
                </Card>
              )}

              {/* Optimal Timeframes */}
              {insights.optimalTimeframes.length > 0 && (
                <Card>
                  <CardHeader>
                    <CardTitle className="flex items-center space-x-2">
                      <Calendar className="h-5 w-5" />
                      <span>Optimal Timeframes</span>
                    </CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="flex flex-wrap gap-2">
                      {insights.optimalTimeframes.map((timeframe, index) => (
                        <Badge key={index} variant="outline">
                          {timeframe}
                        </Badge>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              )}

              {/* Emotional Patterns */}
              {insights.emotionalPatterns && (
                <Card>
                  <CardHeader>
                    <CardTitle>Emotional Patterns</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <p className="text-muted-foreground">{insights.emotionalPatterns}</p>
                  </CardContent>
                </Card>
              )}

              {/* Next Steps */}
              <Card className="border-primary/20 bg-primary/5">
                <CardHeader>
                  <CardTitle>Next Steps</CardTitle>
                </CardHeader>
                <CardContent>
                  <ul className="space-y-2">
                    {insights.nextSteps.map((step, index) => (
                      <li key={index} className="flex items-start space-x-2 text-sm">
                        <span className="text-primary font-semibold mt-1">{index + 1}.</span>
                        <span>{step}</span>
                      </li>
                    ))}
                  </ul>
                </CardContent>
              </Card>
          </>
        )}

        {/* Empty State */}
        {!insights && !loading && (
          <Card>
            <CardContent className="py-12">
              <div className="text-center text-muted-foreground">
                <Brain className="mx-auto h-12 w-12 mb-4 opacity-50" />
                <p className="text-lg">No insights yet</p>
                <p className="text-sm mt-2">Configure filters and click "Generate AI Insights"</p>
              </div>
            </CardContent>
          </Card>
        )}
      </div>
    </AppLayout>
  );
}

