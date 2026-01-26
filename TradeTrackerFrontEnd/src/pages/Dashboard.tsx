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
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:24',message:'loadDashboardData entry',data:{},timestamp:Date.now(),sessionId:'debug-session',runId:'run1',hypothesisId:'H2,H3'})}).catch(()=>{});
    // #endregion
    try {
      const response = await api.getDashboardData();
      // #region agent log
      fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:27',message:'API response received',data:{hasResponse:!!response,responseKeys:Object.keys(response||{}),responseType:typeof response,isArray:Array.isArray(response)},timestamp:Date.now(),sessionId:'debug-session',runId:'run1',hypothesisId:'H2'})}).catch(()=>{});
      // #endregion
      const finalData = response;
      // #region agent log
      fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:28',message:'Data before setState',data:{hasMonthlyPerformance:!!finalData?.monthlyPerformance,monthlyPerformanceLength:finalData?.monthlyPerformance?.length||0,monthlyPerformanceSample:finalData?.monthlyPerformance?.[0]||null,hasRecentTrades:!!finalData?.recentTrades},timestamp:Date.now(),sessionId:'debug-session',runId:'run1',hypothesisId:'H1,H4'})}).catch(()=>{});
      // #endregion
      setData(finalData);
    } catch (error) {
      // #region agent log
      fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:29',message:'Error in loadDashboardData',data:{errorMessage:error instanceof Error?error.message:String(error),errorType:error?.constructor?.name},timestamp:Date.now(),sessionId:'debug-session',runId:'run1',hypothesisId:'H3'})}).catch(()=>{});
      // #endregion
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
          <h1 className="text-3xl font-bold tracking-tight text-primary">Dashboard</h1>
          <p className="text-muted-foreground">Welcome back! Here's your trading overview.</p>
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
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:222',message:'getEquityCurveData called',data:{hasData:!!data,hasMonthlyPerformance:!!data?.monthlyPerformance,monthlyPerformanceLength:data?.monthlyPerformance?.length||0,hasRecentTrades:!!data?.recentTrades,recentTradesLength:data?.recentTrades?.length||0},timestamp:Date.now(),sessionId:'debug-session',runId:'post-fix',hypothesisId:'H1'})}).catch(()=>{});
  // #endregion
  
  // Calculate equity curve from monthly performance (cumulative)
  if (data?.monthlyPerformance && data.monthlyPerformance.length > 0) {
    let cumulative = 0;
    const equityData = data.monthlyPerformance.map((month) => {
      cumulative += month.profitLoss;
      return {
        date: month.month,
        profitLoss: cumulative
      };
    });
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:235',message:'Equity curve from monthly performance',data:{equityDataLength:equityData.length,firstPoint:equityData[0],lastPoint:equityData[equityData.length-1]},timestamp:Date.now(),sessionId:'debug-session',runId:'post-fix',hypothesisId:'H1'})}).catch(()=>{});
    // #endregion
    return equityData;
  }
  
  // Fallback: empty data
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:242',message:'Equity curve fallback - no data',data:{},timestamp:Date.now(),sessionId:'debug-session',runId:'post-fix',hypothesisId:'H1'})}).catch(()=>{});
  // #endregion
  return [{ date: 'No Data', profitLoss: 0 }];
}

function getMonthlyData(data: DashboardData | null) {
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:247',message:'getMonthlyData called',data:{hasData:!!data,hasMonthlyPerformance:!!data?.monthlyPerformance,monthlyPerformanceLength:data?.monthlyPerformance?.length||0,monthlyPerformanceSample:data?.monthlyPerformance?.[0]||null},timestamp:Date.now(),sessionId:'debug-session',runId:'post-fix',hypothesisId:'H1,H4'})}).catch(()=>{});
  // #endregion
  
  // Use monthly performance from API
  if (data?.monthlyPerformance && data.monthlyPerformance.length > 0) {
    const monthlyData = data.monthlyPerformance.map((month) => ({
      name: month.month,
      value: month.profitLoss
    }));
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:255',message:'Monthly data from API',data:{monthlyDataLength:monthlyData.length,firstMonth:monthlyData[0],lastMonth:monthlyData[monthlyData.length-1]},timestamp:Date.now(),sessionId:'debug-session',runId:'post-fix',hypothesisId:'H1,H4'})}).catch(()=>{});
    // #endregion
    return monthlyData;
  }
  
  // Fallback: empty data
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Dashboard.tsx:263',message:'Monthly data fallback - no data',data:{},timestamp:Date.now(),sessionId:'debug-session',runId:'post-fix',hypothesisId:'H1,H4'})}).catch(()=>{});
  // #endregion
  return [{ name: 'No Data', value: 0 }];
}

