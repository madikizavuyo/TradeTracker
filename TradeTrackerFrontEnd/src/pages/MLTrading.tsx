import { useEffect, useState } from 'react';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';
import { Badge } from '@/components/ui/badge';
import { Sparkles, Upload, FileText, Brain, CheckCircle2, AlertCircle, Zap } from 'lucide-react';
import { api } from '@/lib/api';
import { Strategy } from '@/lib/types';
import { formatDate } from '@/lib/utils';

interface MLHistory {
  id: number;
  originalFileName: string;
  tradesImported: number;
  tradesSkipped: number;
  status: string;
  importedAt: string;
}

export default function MLTrading() {
  const [file, setFile] = useState<File | null>(null);
  const [currency, setCurrency] = useState('USD');
  const [selectedStrategy, setSelectedStrategy] = useState<number | undefined>();
  const [strategies, setStrategies] = useState<Strategy[]>([]);
  const [history, setHistory] = useState<MLHistory[]>([]);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      const [strategiesData, historyData] = await Promise.all([
        api.getMLStrategies(),
        api.getMLHistory()
      ]);
      setStrategies(strategiesData || []);
      setHistory(historyData || []);
    } catch (error) {
      console.error('Failed to load data:', error);
    }
  };

  const handleUpload = async () => {
    if (!file) {
      setMessage({ type: 'error', text: 'Please select a file first' });
      return;
    }

    setLoading(true);
    setMessage(null);

    try {
      const response = await api.uploadMLFile(file, currency, selectedStrategy);
      setMessage({
        type: 'success',
        text: `AI processing complete! Extracted trades from "${file.name}"`
      });
      setFile(null);
      
      // Reload history
      setTimeout(() => {
        loadData();
      }, 2000);
    } catch (error: any) {
      setMessage({
        type: 'error',
        text: error.response?.data?.error || 'AI processing failed. Please try again.'
      });
    } finally {
      setLoading(false);
    }
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Header */}
        <div>
          <div className="flex items-center space-x-3 mb-2">
            <Brain className="h-8 w-8 text-primary" />
            <h1 className="text-3xl font-bold tracking-tight text-primary">AI Trade Extraction</h1>
            <Badge variant="default" className="flex items-center space-x-1">
              <Sparkles className="h-3 w-3" />
              <span>DeepSeek AI</span>
            </Badge>
          </div>
          <p className="text-muted-foreground">
            Upload ANY document type - AI will automatically extract trade data
          </p>
        </div>

        {/* Message */}
        {message && (
          <div
            className={`flex items-center space-x-2 p-4 rounded-lg ${
              message.type === 'success'
                ? 'bg-success/10 text-success border border-success/20'
                : 'bg-destructive/10 text-destructive border border-destructive/20'
            }`}
          >
            {message.type === 'success' ? (
              <CheckCircle2 className="h-5 w-5" />
            ) : (
              <AlertCircle className="h-5 w-5" />
            )}
            <span className="font-medium">{message.text}</span>
          </div>
        )}

        {/* Features Card */}
        <Card className="border-primary/20 bg-gradient-to-br from-primary/5 to-transparent">
          <CardHeader>
            <CardTitle className="flex items-center space-x-2">
              <Zap className="h-5 w-5 text-primary" />
              <span>AI-Powered Features</span>
            </CardTitle>
            <CardDescription>Advanced trade extraction capabilities</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="grid gap-3 md:grid-cols-2">
              <div className="flex items-start space-x-3">
                <CheckCircle2 className="h-5 w-5 text-success mt-0.5" />
                <div>
                  <p className="font-medium">Universal File Support</p>
                  <p className="text-sm text-muted-foreground">PDF, images, Word docs, Excel, CSV, and more</p>
                </div>
              </div>
              <div className="flex items-start space-x-3">
                <CheckCircle2 className="h-5 w-5 text-success mt-0.5" />
                <div>
                  <p className="font-medium">Intelligent Extraction</p>
                  <p className="text-sm text-muted-foreground">AI understands context and structure</p>
                </div>
              </div>
              <div className="flex items-start space-x-3">
                <CheckCircle2 className="h-5 w-5 text-success mt-0.5" />
                <div>
                  <p className="font-medium">Duplicate Detection</p>
                  <p className="text-sm text-muted-foreground">Multi-field matching prevents duplicates</p>
                </div>
              </div>
              <div className="flex items-start space-x-3">
                <CheckCircle2 className="h-5 w-5 text-success mt-0.5" />
                <div>
                  <p className="font-medium">Auto-Categorization</p>
                  <p className="text-sm text-muted-foreground">Assigns strategies automatically</p>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Upload Card */}
        <Card>
          <CardHeader>
            <div className="flex items-center space-x-2">
              <Upload className="h-5 w-5 text-primary" />
              <CardTitle>Upload Document</CardTitle>
            </div>
            <CardDescription>
              AI will analyze and extract trade information from any document type
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="file">Select File</Label>
              <Input
                id="file"
                type="file"
                accept="*/*"
                onChange={(e) => setFile(e.target.files?.[0] || null)}
                disabled={loading}
              />
              {file && (
                <div className="flex items-center justify-between text-sm p-3 bg-muted/30 rounded-lg">
                  <div className="flex items-center space-x-2">
                    <FileText className="h-4 w-4 text-primary" />
                    <span className="font-medium">{file.name}</span>
                  </div>
                  <span className="text-muted-foreground">
                    {(file.size / 1024 / 1024).toFixed(2)} MB
                  </span>
                </div>
              )}
            </div>

            <div className="grid gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="currency">Currency</Label>
                <Input
                  id="currency"
                  value={currency}
                  onChange={(e) => setCurrency(e.target.value)}
                  placeholder="USD"
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="strategy">Strategy (Optional)</Label>
                <Select
                  id="strategy"
                  value={selectedStrategy?.toString() || ''}
                  onChange={(e) => setSelectedStrategy(e.target.value ? parseInt(e.target.value) : undefined)}
                >
                  <option value="">Auto-detect</option>
                  {strategies.map((strategy) => (
                    <option key={strategy.id} value={strategy.id}>
                      {strategy.name}
                    </option>
                  ))}
                </Select>
              </div>
            </div>

            <Button onClick={handleUpload} disabled={!file || loading} className="w-full">
              {loading ? (
                <>
                  <Brain className="mr-2 h-4 w-4 animate-pulse" />
                  AI Processing...
                </>
              ) : (
                <>
                  <Sparkles className="mr-2 h-4 w-4" />
                  Extract Trades with AI
                </>
              )}
            </Button>

            <div className="border-t pt-4">
              <h4 className="font-medium mb-2">Supported Document Types</h4>
              <div className="grid gap-2 text-sm">
                <div className="flex items-center space-x-2">
                  <FileText className="h-4 w-4 text-primary" />
                  <span>PDF Documents - Broker statements, reports</span>
                </div>
                <div className="flex items-center space-x-2">
                  <FileText className="h-4 w-4 text-primary" />
                  <span>Images - Screenshots, scanned documents (JPG, PNG)</span>
                </div>
                <div className="flex items-center space-x-2">
                  <FileText className="h-4 w-4 text-primary" />
                  <span>Office Docs - Word, Excel, CSV files</span>
                </div>
                <div className="flex items-center space-x-2">
                  <FileText className="h-4 w-4 text-primary" />
                  <span>Text Files - Any structured trade data</span>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* History */}
        <Card>
          <CardHeader>
            <CardTitle>AI Processing History</CardTitle>
            <CardDescription>Recent AI-powered trade extractions</CardDescription>
          </CardHeader>
          <CardContent>
            {history.length > 0 ? (
              <div className="space-y-3">
                {history.map((item) => (
                  <div
                    key={item.id}
                    className="flex items-center justify-between border rounded-lg p-4 hover:bg-muted/30 transition-colors"
                  >
                    <div className="flex items-center space-x-3">
                      <Brain className="h-5 w-5 text-primary" />
                      <div>
                        <p className="font-medium">{item.originalFileName}</p>
                        <p className="text-sm text-muted-foreground">{formatDate(item.importedAt)}</p>
                      </div>
                    </div>
                    <div className="flex items-center space-x-4">
                      <div className="text-right">
                        <p className="font-medium text-success">{item.tradesImported} trades</p>
                        {item.tradesSkipped > 0 && (
                          <p className="text-xs text-muted-foreground">{item.tradesSkipped} duplicates skipped</p>
                        )}
                      </div>
                      <Badge variant={item.status === 'Completed' ? 'success' : 'default'}>
                        {item.status}
                      </Badge>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="text-center py-12 text-muted-foreground">
                <Brain className="mx-auto h-12 w-12 mb-4 opacity-50" />
                <p>No AI processing history yet</p>
                <p className="text-sm mt-2">Upload your first document to get started</p>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </AppLayout>
  );
}







