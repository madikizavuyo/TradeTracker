import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { PerformanceChart } from '@/components/PerformanceChart';
import { BarChartComponent } from '@/components/BarChartComponent';
import { TrendingUp, TrendingDown, Activity, DollarSign } from 'lucide-react';
import { api } from '@/lib/api';
import { DashboardData, Trade } from '@/lib/types';
import { formatCurrency, formatDate } from '@/lib/utils';

export default function Dashboard() {
  const navigate = useNavigate();
  const [data, setData] = useState<DashboardData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadDashboardData();
  }, []);

  const loadDashboardData = async () => {
    try {
      const response = await api.getDashboardData();
      setData(response);
    } catch (error) {
      console.error('Failed to load dashboard data:', error);
      setError('Failed to load dashboard data. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  if (loading) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-96">
          <div className="text-muted-foreground">Loading dashboard...</div>
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
            <Button onClick={loadDashboardData}>Retry</Button>
          </div>
        </div>
      </AppLayout>
    );
  }

  const currencySymbol = data?.displayCurrencySymbol || '$';

  return (
    <AppLayout>
      <div className="space-y-6">
        <div>
          <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-primary">Dashboard</h1>
          <p className="text-sm sm:text-base text-muted-foreground">Welcome back! Here's your trading overview.</p>
        </div>

        {/* Performance Cards */}
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">Total Trades</CardTitle>
              <Activity className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold">{data?.totalTrades || 0}</div>
              <p className="text-xs text-muted-foreground">
                {data?.openTrades || 0} open positions
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">Win Rate</CardTitle>
              <TrendingUp className="h-4 w-4 text-success" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold">{data?.winRate ? data.winRate.toFixed(1) : '0.0'}%</div>
              <p className="text-xs text-muted-foreground">
                {data?.winningTrades || 0} winning trades
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">Total P&L</CardTitle>
              <DollarSign className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className={`text-2xl font-bold ${(data?.totalProfitLossDisplay || 0) >= 0 ? 'text-success' : 'text-destructive'}`}>
                {formatCurrency(data?.totalProfitLossDisplay || 0, currencySymbol)}
              </div>
              <p className="text-xs text-muted-foreground">
                Display currency: {data?.displayCurrency || 'USD'}
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">Profit Factor</CardTitle>
              <TrendingUp className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold">{data?.profitFactor ? data.profitFactor.toFixed(2) : '0.00'}</div>
              <p className="text-xs text-muted-foreground">
                Avg Win: {formatCurrency(data?.averageWinDisplay || 0, currencySymbol)}
              </p>
            </CardContent>
          </Card>
        </div>

        {/* Recent Trades */}
        <Card>
          <CardHeader>
            <CardTitle>Recent Trades</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-4">
              {data?.recentTrades && data.recentTrades.length > 0 ? (
                data.recentTrades.map((trade) => (
                  <div
                    key={trade.id}
                    className="flex items-center justify-between border-b pb-4 last:border-0"
                  >
                    <div className="space-y-1">
                      <div className="flex items-center space-x-2">
                        <span className="font-semibold">{trade.instrument}</span>
                        <Badge variant={trade.type === 'Long' ? 'success' : 'destructive'}>
                          {trade.type}
                        </Badge>
                        <Badge variant="outline">{trade.status}</Badge>
                      </div>
                      <div className="text-sm text-muted-foreground">
                        Entry: {trade.entryPrice} | Exit: {trade.exitPrice || 'Open'} | {formatDate(trade.dateTime)}
                      </div>
                      {trade.strategyName && (
                        <div className="text-xs text-muted-foreground">
                          Strategy: {trade.strategyName}
                        </div>
                      )}
                    </div>
                    <div className="text-right">
                      <div className={`text-lg font-semibold ${(trade.profitLossDisplay || 0) >= 0 ? 'text-success' : 'text-destructive'}`}>
                        {formatCurrency(trade.profitLossDisplay || 0, currencySymbol)}
                      </div>
                      <div className="text-xs text-muted-foreground">{trade.broker}</div>
                    </div>
                  </div>
                ))
              ) : (
                <div className="text-center text-muted-foreground py-8">
                  No trades yet. Start by adding your first trade!
                </div>
              )}
            </div>
          </CardContent>
        </Card>

        {/* Performance Chart */}
        <Card>
          <CardHeader>
            <CardTitle>Equity Curve</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="h-64">
              <PerformanceChart
                data={getEquityCurveData(data)}
                currencySymbol={currencySymbol}
              />
            </div>
          </CardContent>
        </Card>

        {/* Monthly Performance */}
        <Card>
          <CardHeader>
            <CardTitle>Monthly Performance</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="h-80">
              <BarChartComponent
                data={getMonthlyData(data)}
                currencySymbol={currencySymbol}
                title="Monthly P&L"
              />
            </div>
          </CardContent>
        </Card>
      </div>
    </AppLayout>
  );
}

// Chart data generators
function getEquityCurveData(data: DashboardData | null) {
  if (data?.monthlyPerformance && data.monthlyPerformance.length > 0) {
    let cumulative = 0;
    return data.monthlyPerformance.map((month) => {
      cumulative += month.profitLoss;
      return {
        date: month.month,
        profitLoss: cumulative
      };
    });
  }
  return [{ date: 'No Data', profitLoss: 0 }];
}

function getMonthlyData(data: DashboardData | null) {
  if (data?.monthlyPerformance && data.monthlyPerformance.length > 0) {
    return data.monthlyPerformance.map((month) => ({
      name: month.month,
      value: month.profitLoss
    }));
  }
  return [{ name: 'No Data', value: 0 }];
}

