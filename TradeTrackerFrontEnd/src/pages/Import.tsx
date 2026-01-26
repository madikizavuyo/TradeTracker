import { useEffect, useState } from 'react';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';
import { Badge } from '@/components/ui/badge';
import { Upload, FileSpreadsheet, CheckCircle2, XCircle, Clock, AlertCircle } from 'lucide-react';
import { api } from '@/lib/api';
import { Strategy } from '@/lib/types';
import { formatDate } from '@/lib/utils';

interface ImportHistory {
  id: number;
  brokerName: string;
  originalFileName: string;
  tradesImported: number;
  tradesSkipped: number;
  tradesFailed: number;
  status: string;
  importedAt: string;
  completedAt?: string;
}

export default function Import() {
  const [history, setHistory] = useState<ImportHistory[]>([]);
  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [brokerName, setBrokerName] = useState('');
  const [currency, setCurrency] = useState('USD');
  const [selectedStrategy, setSelectedStrategy] = useState<number | undefined>(undefined);
  const [strategies, setStrategies] = useState<Strategy[]>([]);

  useEffect(() => {
    loadHistory();
    loadStrategies();
  }, []);

  const loadStrategies = async () => {
    try {
      const response: any = await api.getStrategies();
      // Handle different response formats
      let strategiesList: Strategy[] = [];
      if (Array.isArray(response)) {
        strategiesList = response;
      } else if (response?.data && Array.isArray(response.data)) {
        strategiesList = response.data;
      }
      setStrategies(strategiesList);
    } catch (error) {
      console.error('Failed to load strategies:', error);
      setStrategies([]); // Set empty array on error
    }
  };

  const loadHistory = async () => {
    try {
      const response: any = await api.getImportHistory();
      // Handle ApiResponse<T> wrapper or direct array
      const data = Array.isArray(response) ? response : (response?.data || []);
      setHistory(data);
    } catch (error) {
      console.error('Failed to load import history:', error);
      setHistory([]);
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      const selectedFile = e.target.files[0];
      setFile(selectedFile);
      setMessage(null);
    }
  };

  const handleUpload = async () => {
    if (!file) {
      setMessage({ type: 'error', text: 'Please select a file first' });
      return;
    }

    setUploading(true);
    setMessage(null);

    try {
      const columnMappings: Record<string, string> = {};
      if (brokerName) columnMappings.brokerName = brokerName;
      if (currency) columnMappings.currency = currency;
      if (selectedStrategy) columnMappings.strategyId = selectedStrategy.toString();

      const response = await api.uploadImportFile(file, columnMappings);
      
      // Check if it's a PDF file that was accepted
      const fileExtension = file.name.toLowerCase().split('.').pop();
      if (fileExtension === 'pdf' && response?.fileType === 'pdf') {
        setMessage({
          type: 'success',
          text: response.message || `PDF file "${file.name}" uploaded successfully! Note: Automatic trade extraction from PDFs requires AI processing.`
        });
      } else if (response?.tradesImported !== undefined) {
        setMessage({
          type: 'success',
          text: response.message || `File "${file.name}" uploaded successfully! ${response.tradesImported} trades imported, ${response.tradesSkipped || 0} skipped.`
        });
      } else {
        setMessage({
          type: 'success',
          text: `File "${file.name}" uploaded successfully! Processing trades...`
        });
      }
      
      setFile(null);
      setBrokerName('');
      setCurrency('USD');
      setSelectedStrategy(undefined);
      
      // Reload history after successful upload
      setTimeout(() => {
        loadHistory();
      }, 2000);
    } catch (error: any) {
      let errorMessage = error.response?.data?.message || 
                         error.response?.data?.error || 
                         error.message || 
                         'Failed to upload file. Please try again.';
      
      // Check if it's a success message for PDF files (accepted but not parsed)
      if (errorMessage.includes('PDF file accepted') || errorMessage.includes('saved')) {
        setMessage({
          type: 'success',
          text: errorMessage
        });
        // Still reload history
        setTimeout(() => {
          loadHistory();
        }, 2000);
        return;
      }
      
      setMessage({
        type: 'error',
        text: errorMessage
      });
    } finally {
      setUploading(false);
    }
  };

  const getStatusBadge = (status: string) => {
    const statusMap: Record<string, { variant: any; icon: any }> = {
      Completed: { variant: 'success', icon: CheckCircle2 },
      Failed: { variant: 'destructive', icon: XCircle },
      Processing: { variant: 'default', icon: Clock },
      InProgress: { variant: 'default', icon: Clock },
    };

    const config = statusMap[status] || { variant: 'outline', icon: AlertCircle };
    const Icon = config.icon;

    return (
      <Badge variant={config.variant} className="flex items-center space-x-1">
        <Icon className="h-3 w-3" />
        <span>{status}</span>
      </Badge>
    );
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Header */}
        <div>
          <h1 className="text-3xl font-bold tracking-tight text-primary">Import Trades</h1>
          <p className="text-muted-foreground">Upload CSV or Excel files to import your trades</p>
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

        {/* Upload Section */}
        <Card>
          <CardHeader>
            <div className="flex items-center space-x-2">
              <Upload className="h-5 w-5 text-primary" />
              <CardTitle>Upload Trade File</CardTitle>
            </div>
            <CardDescription>
              Support for CSV, Excel (.xlsx, .xls), and PDF files (up to 100MB). Automatic duplicate detection included.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="file">Select File</Label>
              <div className="flex items-center space-x-4">
                <Input
                  id="file"
                  type="file"
                  accept=".csv,.xlsx,.xls,.pdf"
                  onChange={handleFileChange}
                  disabled={uploading}
                  className="flex-1"
                />
                <Button onClick={handleUpload} disabled={!file || uploading}>
                  {uploading ? (
                    <>
                      <Clock className="mr-2 h-4 w-4 animate-spin" />
                      Uploading...
                    </>
                  ) : (
                    <>
                      <Upload className="mr-2 h-4 w-4" />
                      Upload
                    </>
                  )}
                </Button>
              </div>
              {file && (
                <p className="text-sm text-muted-foreground">
                  Selected: {file.name} ({(file.size / 1024 / 1024).toFixed(2)} MB)
                </p>
              )}
            </div>

            <div className="grid gap-4 md:grid-cols-3">
              <div className="space-y-2">
                <Label htmlFor="brokerName">Broker Name</Label>
                <Input
                  id="brokerName"
                  value={brokerName}
                  onChange={(e) => setBrokerName(e.target.value)}
                  placeholder="e.g., XM"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="currency">Currency</Label>
                <Select
                  id="currency"
                  value={currency}
                  onChange={(e) => setCurrency(e.target.value)}
                >
                  <option value="USD">USD - US Dollar</option>
                  <option value="EUR">EUR - Euro</option>
                  <option value="GBP">GBP - British Pound</option>
                  <option value="JPY">JPY - Japanese Yen</option>
                  <option value="ZAR">ZAR - South African Rand</option>
                  <option value="AUD">AUD - Australian Dollar</option>
                  <option value="CAD">CAD - Canadian Dollar</option>
                  <option value="CHF">CHF - Swiss Franc</option>
                </Select>
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

            <div className="border-t pt-4">
              <h4 className="font-medium mb-2">Supported Formats</h4>
              <div className="grid gap-2 md:grid-cols-3 text-sm">
                <div className="flex items-center space-x-2">
                  <FileSpreadsheet className="h-4 w-4 text-success" />
                  <span>CSV Files (.csv)</span>
                </div>
                <div className="flex items-center space-x-2">
                  <FileSpreadsheet className="h-4 w-4 text-success" />
                  <span>Excel Files (.xlsx)</span>
                </div>
                <div className="flex items-center space-x-2">
                  <FileSpreadsheet className="h-4 w-4 text-success" />
                  <span>Legacy Excel (.xls)</span>
                </div>
                <div className="flex items-center space-x-2">
                  <FileSpreadsheet className="h-4 w-4 text-success" />
                  <span>PDF Files (.pdf)</span>
                </div>
              </div>
            </div>

            <div className="bg-muted/30 border rounded-lg p-4">
              <h4 className="font-medium mb-2 text-sm">Import Tips</h4>
              <ul className="text-xs text-muted-foreground space-y-1 list-disc list-inside">
                <li>Ensure your file has headers (symbol, entry price, exit price, etc.)</li>
                <li>Dates should be in a standard format (YYYY-MM-DD or MM/DD/YYYY)</li>
                <li>Duplicate trades are automatically detected and skipped</li>
                <li>Maximum file size: 100MB</li>
              </ul>
            </div>
          </CardContent>
        </Card>

        {/* Import History */}
        <Card>
          <CardHeader>
            <CardTitle>Import History</CardTitle>
            <CardDescription>Recent trade imports and their results</CardDescription>
          </CardHeader>
          <CardContent>
            {history.length > 0 ? (
              <div className="space-y-4">
                {history.map((item) => (
                  <div
                    key={item.id}
                    className="flex items-center justify-between border rounded-lg p-4 hover:bg-muted/30 transition-colors"
                  >
                    <div className="flex-1 space-y-1">
                      <div className="flex items-center space-x-3">
                        <FileSpreadsheet className="h-5 w-5 text-primary" />
                        <span className="font-medium">{item.originalFileName}</span>
                        {getStatusBadge(item.status)}
                      </div>
                      <div className="text-sm text-muted-foreground">
                        Broker: {item.brokerName} • {formatDate(item.importedAt)}
                      </div>
                    </div>

                    <div className="text-right space-y-1">
                      <div className="flex items-center justify-end space-x-4 text-sm">
                        <div className="flex items-center space-x-1">
                          <CheckCircle2 className="h-4 w-4 text-success" />
                          <span className="text-success font-medium">{item.tradesImported}</span>
                          <span className="text-muted-foreground">imported</span>
                        </div>
                        {item.tradesSkipped > 0 && (
                          <div className="flex items-center space-x-1">
                            <Clock className="h-4 w-4 text-muted-foreground" />
                            <span className="font-medium">{item.tradesSkipped}</span>
                            <span className="text-muted-foreground">skipped</span>
                          </div>
                        )}
                        {item.tradesFailed > 0 && (
                          <div className="flex items-center space-x-1">
                            <XCircle className="h-4 w-4 text-destructive" />
                            <span className="text-destructive font-medium">{item.tradesFailed}</span>
                            <span className="text-muted-foreground">failed</span>
                          </div>
                        )}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="text-center py-12 text-muted-foreground">
                <FileSpreadsheet className="mx-auto h-12 w-12 mb-4 opacity-50" />
                <p>No import history yet</p>
                <p className="text-sm mt-2">Upload your first file to get started</p>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </AppLayout>
  );
}


