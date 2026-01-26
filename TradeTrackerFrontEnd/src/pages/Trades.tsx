import { useEffect, useState, useCallback } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Select } from '@/components/ui/select';
import { Pagination } from '@/components/ui/pagination';
import { Plus, Search } from 'lucide-react';
import { api } from '@/lib/api';
import { Trade } from '@/lib/types';
import { formatCurrency, formatDate } from '@/lib/utils';

export default function Trades() {
  const navigate = useNavigate();
  const [trades, setTrades] = useState<Trade[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [instrumentFilter, setInstrumentFilter] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [sortBy, setSortBy] = useState('date');
  const [sortOrder, setSortOrder] = useState('desc');
  
  // Pagination
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(0);

  const loadTrades = useCallback(async () => {
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Trades.tsx:38',message:'loadTrades entry',data:{currentPage,pageSize,filters:{pageNumber:currentPage,pageSize,search:searchTerm||undefined,status:statusFilter!=='all'?statusFilter:undefined}},timestamp:Date.now(),sessionId:'debug-session',runId:'run1',hypothesisId:'H5,H8'})}).catch(()=>{});
    // #endregion
    try {
      setLoading(true);
      const filters = {
        pageNumber: currentPage,
        pageSize: pageSize,
        search: searchTerm || undefined,
        status: statusFilter !== 'all' ? statusFilter : undefined,
        instrument: instrumentFilter || undefined,
        startDate: startDate || undefined,
        endDate: endDate || undefined,
        sortBy,
        sortOrder
      };

      const response = await api.getTrades(filters);
      // #region agent log
      fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Trades.tsx:53',message:'API response received',data:{hasItems:!!response.items,hasTrades:!!response.trades,itemsLength:response.items?.length||0,tradesLength:response.trades?.length||0,hasTotalCount:!!response.totalCount,totalCount:response.totalCount,hasTotalPages:!!response.totalPages,totalPages:response.totalPages,responseKeys:Object.keys(response||{})},timestamp:Date.now(),sessionId:'debug-session',runId:'run1',hypothesisId:'H6'})}).catch(()=>{});
      // #endregion
      
      // Response is already normalized by getTrades method
      const tradesArray = response.items || response.trades || [];
      const totalCountValue = response.totalCount || tradesArray.length;
      const totalPagesValue = response.totalPages || Math.ceil(totalCountValue / pageSize);
      // #region agent log
      fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Trades.tsx:58',message:'Before setState',data:{tradesArrayLength:tradesArray.length,totalCountValue,totalPagesValue,pageSize,calculation:Math.ceil(totalCountValue/pageSize)},timestamp:Date.now(),sessionId:'debug-session',runId:'run1',hypothesisId:'H6,H7'})}).catch(()=>{});
      // #endregion
      setTrades(tradesArray);
      setTotalCount(totalCountValue);
      setTotalPages(totalPagesValue);
      setError(null);
    } catch (error: any) {
      // #region agent log
      fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Trades.tsx:60',message:'Error in loadTrades',data:{errorMessage:error?.response?.data?.message||error?.message||String(error),errorType:error?.constructor?.name,statusCode:error?.response?.status},timestamp:Date.now(),sessionId:'debug-session',runId:'run1',hypothesisId:'H6'})}).catch(()=>{});
      // #endregion
      console.error('Failed to load trades:', error);
      const errorMessage = error.response?.data?.message || 
                          error.message || 
                          'Failed to load trades. Please try again.';
      setError(errorMessage);
    } finally {
      setLoading(false);
    }
  }, [currentPage, pageSize, statusFilter, searchTerm, instrumentFilter, startDate, endDate, sortBy, sortOrder]);

  useEffect(() => {
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/1fee1a4b-310a-4c60-9f48-bebb8e3622bd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({location:'Trades.tsx:34',message:'useEffect triggered',data:{currentPage,pageSize,statusFilter,searchTerm,instrumentFilter,startDate,endDate,sortBy,sortOrder},timestamp:Date.now(),sessionId:'debug-session',runId:'post-fix',hypothesisId:'H5,H8'})}).catch(()=>{});
    // #endregion
    loadTrades();
  }, [loadTrades]);

  const handleFilterReset = () => {
    setSearchTerm('');
    setStatusFilter('all');
    setInstrumentFilter('');
    setStartDate('');
    setEndDate('');
    setSortBy('date');
    setSortOrder('desc');
    setCurrentPage(1);
  };

  if (loading) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-96">
          <div className="text-muted-foreground">Loading trades...</div>
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
            <Button onClick={loadTrades}>Retry</Button>
          </div>
        </div>
      </AppLayout>
    );
  }

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-primary">Trades</h1>
            <p className="text-muted-foreground">Manage and track all your trading activity</p>
          </div>
          <Link to="/trades/new">
            <Button>
              <Plus className="mr-2 h-4 w-4" />
              Add Trade
            </Button>
          </Link>
        </div>

        {/* Search and Filters */}
        <Card>
          <CardContent className="pt-6 space-y-4">
            <div className="flex gap-4">
              <div className="relative flex-1">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                  placeholder="Search by symbol or notes..."
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  className="pl-9"
                />
              </div>
              <Select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                className="w-48"
              >
                <option value="all">All Status</option>
                <option value="Open">Open</option>
                <option value="Closed">Closed</option>
                <option value="Cancelled">Cancelled</option>
              </Select>
              <Select
                value={sortBy}
                onChange={(e) => setSortBy(e.target.value)}
                className="w-40"
              >
                <option value="date">Sort by Date</option>
                <option value="instrument">Sort by Instrument</option>
                <option value="profit">Sort by Profit</option>
              </Select>
            </div>

            <div className="grid gap-4 md:grid-cols-4">
              <div className="space-y-2">
                <label className="text-sm font-medium">Instrument</label>
                <Input
                  placeholder="e.g. EURUSD"
                  value={instrumentFilter}
                  onChange={(e) => setInstrumentFilter(e.target.value)}
                />
              </div>
              <div className="space-y-2">
                <label className="text-sm font-medium">Start Date</label>
                <Input
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                />
              </div>
              <div className="space-y-2">
                <label className="text-sm font-medium">End Date</label>
                <Input
                  type="date"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                />
              </div>
              <div className="space-y-2 flex items-end">
                <Button variant="outline" onClick={handleFilterReset} className="w-full">
                  Reset Filters
                </Button>
              </div>
            </div>

            <div className="text-sm text-muted-foreground">
              Showing {trades.length} of {totalCount} trades
            </div>
          </CardContent>
        </Card>

        {/* Trades Grid */}
        {trades.length > 0 ? (
          <>
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              {trades.map((trade) => (
              <Card
                key={trade.id}
                className="cursor-pointer hover:shadow-lg transition-shadow"
                onClick={() => navigate(`/trades/${trade.id}`)}
              >
                <CardContent className="pt-6">
                  <div className="space-y-4">
                    <div className="flex items-start justify-between">
                      <div>
                        <h3 className="text-xl font-bold">{trade.instrument}</h3>
                        <p className="text-sm text-muted-foreground">{trade.broker}</p>
                      </div>
                      <div className="flex flex-col items-end space-y-1">
                        <Badge variant={trade.type === 'Long' ? 'success' : 'destructive'}>
                          {trade.type}
                        </Badge>
                        <Badge variant="outline">{trade.status}</Badge>
                      </div>
                    </div>

                    <div className="space-y-2 text-sm">
                      <div className="flex justify-between">
                        <span className="text-muted-foreground">Entry:</span>
                        <span className="font-medium">{trade.entryPrice}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-muted-foreground">Exit:</span>
                        <span className="font-medium">{trade.exitPrice || 'Open'}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-muted-foreground">Date:</span>
                        <span className="font-medium">{formatDate(trade.dateTime)}</span>
                      </div>
                      {trade.strategyName && (
                        <div className="flex justify-between">
                          <span className="text-muted-foreground">Strategy:</span>
                          <span className="font-medium">{trade.strategyName}</span>
                        </div>
                      )}
                    </div>

                    <div className="border-t pt-4">
                      <div className="flex items-center justify-between">
                        <span className="text-sm text-muted-foreground">P&L</span>
                        <span className={`text-lg font-bold ${(trade.profitLossDisplay || 0) >= 0 ? 'text-success' : 'text-destructive'}`}>
                          {formatCurrency(trade.profitLossDisplay || 0, trade.displayCurrencySymbol || '$')}
                        </span>
                      </div>
                    </div>
                  </div>
                </CardContent>
              </Card>
            ))}
            </div>

            {/* Pagination */}
            {totalPages > 1 && (
              <Pagination
                currentPage={currentPage}
                totalPages={totalPages}
                totalItems={totalCount}
                pageSize={pageSize}
                onPageChange={setCurrentPage}
                onPageSizeChange={(size) => {
                  setPageSize(size);
                  setCurrentPage(1);
                }}
              />
            )}
          </>
        ) : (
          <Card>
            <CardContent className="py-12">
              <div className="text-center text-muted-foreground">
                <p className="text-lg">No trades found</p>
                <p className="text-sm mt-2">Try adjusting your search or filters</p>
              </div>
            </CardContent>
          </Card>
        )}
      </div>
    </AppLayout>
  );
}
