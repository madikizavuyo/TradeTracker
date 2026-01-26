import { useEffect, useState } from 'react';
import { useNavigate, useParams, Link } from 'react-router-dom';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { ConfirmDialog } from '@/components/ui/dialog';
import { ArrowLeft, Edit, Trash2, Image as ImageIcon, Download, X } from 'lucide-react';
import { api } from '@/lib/api';
import { Trade } from '@/lib/types';
import { formatCurrency, formatDate } from '@/lib/utils';

export default function TradeDetails() {
  const navigate = useNavigate();
  const { id } = useParams();
  const [trade, setTrade] = useState<Trade | null>(null);
  const [loading, setLoading] = useState(true);
  const [deleting, setDeleting] = useState(false);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [selectedImage, setSelectedImage] = useState<string | null>(null);

  useEffect(() => {
    if (id) {
      loadTrade();
    }
  }, [id]);

  const loadTrade = async () => {
    try {
      const tradeData = await api.getTradeDetails(parseInt(id!));
      setTrade(tradeData);
    } catch (error) {
      console.error('Failed to load trade:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleDelete = async () => {
    if (!id) return;

    setDeleting(true);
    try {
      await api.deleteTrade(parseInt(id));
      navigate('/trades');
    } catch (error) {
      console.error('Failed to delete trade:', error);
      alert('Failed to delete trade. Please try again.');
    } finally {
      setDeleting(false);
    }
  };

  const getImageUrl = (imageId: number) => {
    return `${import.meta.env.VITE_API_BASE_URL || 'https://localhost:7000'}/api/Trades/${id}/images/${imageId}`;
  };

  if (loading) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-96">
          <div className="text-muted-foreground">Loading trade details...</div>
        </div>
      </AppLayout>
    );
  }

  if (!trade) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-96">
          <div className="text-center">
            <p className="text-destructive mb-4">Trade not found</p>
            <Link to="/trades">
              <Button>Back to Trades</Button>
            </Link>
          </div>
        </div>
      </AppLayout>
    );
  }

  const currencySymbol = trade.displayCurrencySymbol || '$';
  const isProfit = (trade.profitLossDisplay || 0) >= 0;

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-4">
            <Link to="/trades">
              <Button variant="ghost" size="icon">
                <ArrowLeft className="h-5 w-5" />
              </Button>
            </Link>
            <div>
              <h1 className="text-3xl font-bold tracking-tight text-primary">{trade.instrument}</h1>
              <p className="text-muted-foreground">Trade Details</p>
            </div>
          </div>
          <div className="flex space-x-2">
            <Button variant="outline" onClick={() => navigate(`/trades/${id}/edit`)}>
              <Edit className="mr-2 h-4 w-4" />
              Edit
            </Button>
            <Button variant="destructive" onClick={() => setDeleteDialogOpen(true)}>
              <Trash2 className="mr-2 h-4 w-4" />
              Delete
            </Button>
          </div>
        </div>

        {/* Confirmation Dialog */}
        <ConfirmDialog
          open={deleteDialogOpen}
          onOpenChange={setDeleteDialogOpen}
          title="Delete Trade"
          description="Are you sure you want to delete this trade? This action cannot be undone."
          confirmText="Delete"
          cancelText="Cancel"
          onConfirm={handleDelete}
          variant="destructive"
        />

        {/* Image Lightbox */}
        {selectedImage && (
          <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/90"
            onClick={() => setSelectedImage(null)}
          >
            <button
              className="absolute top-4 right-4 text-white hover:text-gray-300"
              onClick={() => setSelectedImage(null)}
            >
              <X className="h-6 w-6" />
            </button>
            <img
              src={selectedImage}
              alt="Trade screenshot"
              className="max-w-[90vw] max-h-[90vh] object-contain"
              onClick={(e) => e.stopPropagation()}
            />
          </div>
        )}

        {/* Basic Info */}
        <div className="grid gap-6 md:grid-cols-3">
          <Card>
            <CardHeader>
              <CardTitle>Trade Information</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              <div>
                <span className="text-sm text-muted-foreground">Status</span>
                <div className="mt-1">
                  <Badge variant={trade.status === 'Closed' ? 'success' : 'outline'}>
                    {trade.status}
                  </Badge>
                </div>
              </div>
              <div>
                <span className="text-sm text-muted-foreground">Type</span>
                <div className="mt-1">
                  <Badge variant={trade.type === 'Long' ? 'success' : 'destructive'}>
                    {trade.type}
                  </Badge>
                </div>
              </div>
              <div>
                <span className="text-sm text-muted-foreground">Instrument</span>
                <p className="mt-1 font-semibold">{trade.instrument}</p>
              </div>
              {trade.strategyName && (
                <div>
                  <span className="text-sm text-muted-foreground">Strategy</span>
                  <p className="mt-1 font-medium">{trade.strategyName}</p>
                </div>
              )}
              {trade.broker && (
                <div>
                  <span className="text-sm text-muted-foreground">Broker</span>
                  <p className="mt-1 font-medium">{trade.broker}</p>
                </div>
              )}
              <div>
                <span className="text-sm text-muted-foreground">Currency</span>
                <p className="mt-1">{trade.currency} → {trade.displayCurrency}</p>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Entry Details</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              <div>
                <span className="text-sm text-muted-foreground">Entry Price</span>
                <p className="mt-1 text-lg font-semibold">{trade.entryPrice.toFixed(5)}</p>
              </div>
              <div>
                <span className="text-sm text-muted-foreground">Entry Date</span>
                <p className="mt-1 font-medium">{formatDate(trade.dateTime)}</p>
              </div>
              {trade.stopLoss && (
                <div>
                  <span className="text-sm text-muted-foreground">Stop Loss</span>
                  <p className="mt-1 font-medium text-destructive">{trade.stopLoss.toFixed(5)}</p>
                </div>
              )}
              {trade.takeProfit && (
                <div>
                  <span className="text-sm text-muted-foreground">Take Profit</span>
                  <p className="mt-1 font-medium text-success">{trade.takeProfit.toFixed(5)}</p>
                </div>
              )}
              {trade.lotSize && (
                <div>
                  <span className="text-sm text-muted-foreground">Lot Size</span>
                  <p className="mt-1 font-medium">{trade.lotSize.toFixed(2)}</p>
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Exit Details</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              {trade.exitPrice && (
                <div>
                  <span className="text-sm text-muted-foreground">Exit Price</span>
                  <p className="mt-1 text-lg font-semibold">{trade.exitPrice.toFixed(5)}</p>
                </div>
              )}
              {trade.exitDateTime && (
                <div>
                  <span className="text-sm text-muted-foreground">Exit Date</span>
                  <p className="mt-1 font-medium">{formatDate(trade.exitDateTime)}</p>
                </div>
              )}
              <div>
                <span className="text-sm text-muted-foreground">Profit/Loss</span>
                <p className={`mt-1 text-2xl font-bold ${isProfit ? 'text-success' : 'text-destructive'}`}>
                  {formatCurrency(trade.profitLossDisplay || 0, currencySymbol)}
                </p>
                {trade.profitLossDisplay && trade.profitLossDisplay !== trade.profitLoss && (
                  <p className="text-xs text-muted-foreground mt-1">
                    Original: {formatCurrency(trade.profitLoss || 0, trade.currency)}
                  </p>
                )}
              </div>
              {trade.riskReward && (
                <div>
                  <span className="text-sm text-muted-foreground">Risk/Reward Ratio</span>
                  <p className="mt-1 font-medium">{trade.riskReward.toFixed(2)}</p>
                </div>
              )}
            </CardContent>
          </Card>
        </div>

        {/* Notes */}
        {trade.notes && (
          <Card>
            <CardHeader>
              <CardTitle>Trade Notes</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="whitespace-pre-wrap">{trade.notes}</p>
            </CardContent>
          </Card>
        )}

        {/* Images */}
        {trade.tradeImages && trade.tradeImages.length > 0 && (
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center space-x-2">
                <ImageIcon className="h-5 w-5" />
                <span>Trade Screenshots</span>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
                {trade.tradeImages.map((image) => (
                  <div
                    key={image.id}
                    className="relative aspect-video bg-muted rounded-lg overflow-hidden group cursor-pointer"
                    onClick={() => setSelectedImage(getImageUrl(image.id))}
                  >
                    <div className="absolute inset-0 flex items-center justify-center">
                      <ImageIcon className="h-12 w-12 text-muted-foreground" />
                    </div>
                    <div className="absolute bottom-2 left-2 right-2">
                      <Badge variant="outline" className="w-full">
                        {image.imageType} Screenshot
                      </Badge>
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        )}

        {/* Metadata */}
        <Card>
          <CardHeader>
            <CardTitle>Metadata</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2 text-sm">
            {trade.createdAt && (
              <div className="flex justify-between">
                <span className="text-muted-foreground">Created</span>
                <span>{formatDate(trade.createdAt)}</span>
              </div>
            )}
            {trade.updatedAt && (
              <div className="flex justify-between">
                <span className="text-muted-foreground">Last Updated</span>
                <span>{formatDate(trade.updatedAt)}</span>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </AppLayout>
  );
}

