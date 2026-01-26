import { useState, useEffect } from 'react';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/select';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Activity, Upload, Link as LinkIcon, CheckCircle2, AlertCircle, FileText, Sparkles } from 'lucide-react';
import { api } from '@/lib/api';
import { Strategy } from '@/lib/types';

export default function MT5Integration() {
  const [accountNumber, setAccountNumber] = useState('');
  const [password, setPassword] = useState('');
  const [server, setServer] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  
  const [file, setFile] = useState<File | null>(null);
  const [currency, setCurrency] = useState('USD');
  const [useAI, setUseAI] = useState(false);
  const [selectedStrategy, setSelectedStrategy] = useState<number | undefined>(undefined);
  const [strategies, setStrategies] = useState<Strategy[]>([]);
  
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [accountInfo, setAccountInfo] = useState<any>(null);

  useEffect(() => {
    loadStrategies();
  }, []);

  const loadStrategies = async () => {
    try {
      const response: any = await api.getStrategies();
      let strategiesList: Strategy[] = [];
      if (Array.isArray(response)) {
        strategiesList = response;
      } else if (response?.data && Array.isArray(response.data)) {
        strategiesList = response.data;
      }
      setStrategies(strategiesList);
    } catch (error) {
      console.error('Failed to load strategies:', error);
      setStrategies([]);
    }
  };

  const handleTestConnection = async () => {
    if (!accountNumber || !password || !server) {
      setMessage({ type: 'error', text: 'Please fill in all connection fields' });
      return;
    }

    setLoading(true);
    setMessage(null);

    try {
      const response = await api.testMT5Connection(accountNumber, password, server);
      setMessage({ type: 'success', text: 'Connection successful!' });
      
      // Try to get account info
      const info = await api.getMT5AccountInfo(accountNumber, server);
      setAccountInfo(info);
    } catch (error: any) {
      setMessage({ type: 'error', text: error.response?.data?.error || 'Connection failed' });
    } finally {
      setLoading(false);
    }
  };

  const handleImportFromMT5 = async () => {
    if (!accountNumber || !password || !server || !fromDate || !toDate) {
      setMessage({ type: 'error', text: 'Please fill in all fields' });
      return;
    }

    setLoading(true);
    setMessage(null);

    try {
      await api.importFromMT5(accountNumber, password, server, fromDate, toDate);
      setMessage({ type: 'success', text: 'Import completed successfully!' });
    } catch (error: any) {
      setMessage({ type: 'error', text: error.response?.data?.error || 'Import failed' });
    } finally {
      setLoading(false);
    }
  };

  const handleFileUpload = async () => {
    if (!file) {
      setMessage({ type: 'error', text: 'Please select a file' });
      return;
    }

    setLoading(true);
    setMessage(null);

    try {
      await api.uploadMT5File(file, currency, selectedStrategy, useAI);
      setMessage({
        type: 'success',
        text: `File uploaded successfully! ${useAI ? 'AI processing enabled' : 'Standard processing'}`
      });
      setFile(null);
      setSelectedStrategy(undefined);
    } catch (error: any) {
      setMessage({ type: 'error', text: error.response?.data?.error || 'Upload failed' });
    } finally {
      setLoading(false);
    }
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Header */}
        <div>
          <h1 className="text-3xl font-bold tracking-tight text-primary">MetaTrader 5 Integration</h1>
          <p className="text-muted-foreground">Connect your MT5 account or import statement files</p>
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

        <Tabs defaultValue="direct" className="space-y-6">
          <TabsList className="grid w-full grid-cols-2">
            <TabsTrigger value="direct" className="flex items-center space-x-2">
              <LinkIcon className="h-4 w-4" />
              <span>Direct Connection</span>
            </TabsTrigger>
            <TabsTrigger value="file" className="flex items-center space-x-2">
              <Upload className="h-4 w-4" />
              <span>File Upload</span>
            </TabsTrigger>
          </TabsList>

          {/* Direct Connection Tab */}
          <TabsContent value="direct" className="space-y-6">
            <Card>
              <CardHeader>
                <CardTitle>Connect to MT5 Account</CardTitle>
                <CardDescription>
                  Import trades directly from your MetaTrader 5 account
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="grid gap-4 md:grid-cols-3">
                  <div className="space-y-2">
                    <Label htmlFor="accountNumber">Account Number</Label>
                    <Input
                      id="accountNumber"
                      value={accountNumber}
                      onChange={(e) => setAccountNumber(e.target.value)}
                      placeholder="12345678"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="password">Password</Label>
                    <Input
                      id="password"
                      type="password"
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      placeholder="••••••••"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="server">Server</Label>
                    <Input
                      id="server"
                      value={server}
                      onChange={(e) => setServer(e.target.value)}
                      placeholder="XM-Global"
                    />
                  </div>
                </div>

                <Button onClick={handleTestConnection} variant="outline" disabled={loading}>
                  <Activity className="mr-2 h-4 w-4" />
                  Test Connection
                </Button>

                {accountInfo && (
                  <div className="border rounded-lg p-4 bg-success/5">
                    <h4 className="font-medium mb-2 flex items-center space-x-2">
                      <CheckCircle2 className="h-4 w-4 text-success" />
                      <span>Account Connected</span>
                    </h4>
                    <div className="grid gap-2 text-sm">
                      <div className="flex justify-between">
                        <span className="text-muted-foreground">Balance:</span>
                        <span className="font-medium">{accountInfo.currency} {accountInfo.balance}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-muted-foreground">Equity:</span>
                        <span className="font-medium">{accountInfo.currency} {accountInfo.equity}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-muted-foreground">Free Margin:</span>
                        <span className="font-medium">{accountInfo.currency} {accountInfo.freeMargin}</span>
                      </div>
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Import Trade History</CardTitle>
                <CardDescription>Select date range to import trades</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="grid gap-4 md:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="fromDate">From Date</Label>
                    <Input
                      id="fromDate"
                      type="date"
                      value={fromDate}
                      onChange={(e) => setFromDate(e.target.value)}
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="toDate">To Date</Label>
                    <Input
                      id="toDate"
                      type="date"
                      value={toDate}
                      onChange={(e) => setToDate(e.target.value)}
                    />
                  </div>
                </div>

                <Button onClick={handleImportFromMT5} disabled={loading}>
                  {loading ? 'Importing...' : 'Import Trades'}
                </Button>
              </CardContent>
            </Card>
          </TabsContent>

          {/* File Upload Tab */}
          <TabsContent value="file" className="space-y-6">
            <Card>
              <CardHeader>
                <CardTitle>Upload MT5 Statement</CardTitle>
                <CardDescription>
                  Upload CSV, Excel, or PDF statement files. AI processing available for PDFs.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="file">Select File</Label>
                  <Input
                    id="file"
                    type="file"
                    accept=".csv,.xlsx,.xls,.pdf"
                    onChange={(e) => {
                      const file = e.target.files?.[0] || null;
                      setFile(file);
                      // Auto-enable AI processing for PDFs
                      if (file?.name.endsWith('.pdf')) {
                        setUseAI(true);
                      }
                    }}
                    disabled={loading}
                  />
                  {file && (
                    <p className="text-sm text-muted-foreground">
                      Selected: {file.name} ({(file.size / 1024 / 1024).toFixed(2)} MB)
                    </p>
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
                      <option value="">No Strategy</option>
                      {strategies.map((strategy) => (
                        <option key={strategy.id} value={strategy.id}>
                          {strategy.name}
                        </option>
                      ))}
                    </Select>
                  </div>
                </div>

                {file?.name.endsWith('.pdf') && (
                  <div className="flex items-center space-x-2">
                    <input
                      id="useAI"
                      type="checkbox"
                      checked={useAI}
                      onChange={(e) => setUseAI(e.target.checked)}
                      className="h-4 w-4 rounded border-input"
                    />
                    <label htmlFor="useAI" className="text-sm font-medium cursor-pointer flex items-center space-x-2">
                      <Sparkles className="h-4 w-4 text-primary" />
                      <span>Use AI Processing (Recommended for PDFs)</span>
                    </label>
                  </div>
                )}

                <Button onClick={handleFileUpload} disabled={!file || loading}>
                  {loading ? 'Uploading...' : 'Upload & Process'}
                </Button>

                <div className="border-t pt-4">
                  <h4 className="font-medium mb-2">Supported Formats</h4>
                  <div className="grid gap-2 text-sm">
                    <div className="flex items-center space-x-2">
                      <FileText className="h-4 w-4 text-success" />
                      <span>CSV Files - Standard processing</span>
                    </div>
                    <div className="flex items-center space-x-2">
                      <FileText className="h-4 w-4 text-success" />
                      <span>Excel Files (.xlsx, .xls) - Standard processing</span>
                    </div>
                    <div className="flex items-center space-x-2">
                      <Sparkles className="h-4 w-4 text-primary" />
                      <span>PDF Files - AI processing with DeepSeek</span>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>
          </TabsContent>
        </Tabs>
      </div>
    </AppLayout>
  );
}


