import { useEffect, useState } from 'react';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/select';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Settings as SettingsIcon, DollarSign, Check, AlertCircle, RefreshCw } from 'lucide-react';
import { api } from '@/lib/api';
import { useAuth } from '@/lib/AuthContext';

interface Currency {
  code: string;
  name: string;
  symbol: string;
}

export default function Settings() {
  const { isAdmin } = useAuth();
  const [currencies, setCurrencies] = useState<Currency[]>([]);
  const [selectedCurrency, setSelectedCurrency] = useState('USD');
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // Currency test
  const [testFrom, setTestFrom] = useState('USD');
  const [testTo, setTestTo] = useState('ZAR');
  const [testAmount, setTestAmount] = useState('100');
  const [testResult, setTestResult] = useState<any>(null);

  const [dataLoadLoading, setDataLoadLoading] = useState(false);
  const [dataLoadMessage, setDataLoadMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    try {
      const [settingsData, currenciesData] = await Promise.all([
        api.getSettings(),
        api.getAvailableCurrencies()
      ]);
      
      setSelectedCurrency(settingsData.displayCurrency || 'USD');
      setCurrencies(currenciesData || getDefaultCurrencies());
    } catch (error) {
      console.error('Failed to load settings:', error);
      setCurrencies(getDefaultCurrencies());
    }
  };

  const handleUpdateCurrency = async () => {
    setLoading(true);
    setMessage(null);

    try {
      await api.updateCurrency(selectedCurrency);
      setMessage({ type: 'success', text: `Currency updated to ${selectedCurrency} successfully!` });
    } catch (error) {
      setMessage({ type: 'error', text: 'Failed to update currency. Please try again.' });
    } finally {
      setLoading(false);
    }
  };

  const handleTestConversion = async () => {
    const amount = parseFloat(testAmount);
    if (isNaN(amount) || amount <= 0) {
      setTestResult({ success: false, error: 'Please enter a valid amount' });
      return;
    }
    try {
      const result = await api.testCurrencyConversion(testFrom, testTo, amount);
      setTestResult(result);
    } catch (error: any) {
      console.error('Currency test failed:', error);
      const res = error?.response?.data;
      const msg = res?.error || res?.message || error?.message || 'Conversion test failed. Check you are logged in and the API is running.';
      setTestResult({ success: false, error: msg });
    }
  };

  const handleRunDataLoad = async () => {
    setDataLoadLoading(true);
    setDataLoadMessage(null);
    try {
      await api.refreshTrailBlazer();
      setDataLoadMessage({
        type: 'success',
        text: 'Data load started. The job fetches heatmap, COT, MyFXBook, and computes scores.',
      });
    } catch (error) {
      console.error('Data load failed:', error);
      setDataLoadMessage({
        type: 'error',
        text: 'Failed to start data load. Ensure you are logged in and the API is running.',
      });
    } finally {
      setDataLoadLoading(false);
    }
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center space-x-3">
          <SettingsIcon className="h-8 w-8 text-primary" />
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-primary">Settings</h1>
            <p className="text-muted-foreground">Manage your preferences and display options</p>
          </div>
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
              <Check className="h-5 w-5" />
            ) : (
              <AlertCircle className="h-5 w-5" />
            )}
            <span className="font-medium">{message.text}</span>
          </div>
        )}

        <div className="space-y-6">
        {/* Currency Settings */}
        <Card>
          <CardHeader>
            <CardTitle>Display Currency</CardTitle>
            <CardDescription>
              Select the currency for displaying profit/loss and account values
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="currency">Preferred Currency</Label>
              <div className="flex space-x-4">
                <Select
                  id="currency"
                  value={selectedCurrency}
                  onChange={(e) => setSelectedCurrency(e.target.value)}
                  className="flex-1"
                >
                  {currencies.map((currency) => (
                    <option key={currency.code} value={currency.code}>
                      {currency.code} - {currency.name} ({currency.symbol})
                    </option>
                  ))}
                </Select>
                <Button onClick={handleUpdateCurrency} disabled={loading}>
                  {loading ? 'Updating...' : 'Update'}
                </Button>
              </div>
            </div>

            <div className="border-t pt-4">
              <h4 className="font-medium mb-2">Supported Currencies</h4>
              <div className="flex flex-wrap gap-2">
                {currencies.map((currency) => (
                  <Badge key={currency.code} variant="outline">
                    {currency.symbol} {currency.code}
                  </Badge>
                ))}
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Currency Converter Test */}
        <Card>
          <CardHeader>
            <div className="flex items-center space-x-2">
              <DollarSign className="h-5 w-5 text-primary" />
              <CardTitle>Currency Converter (Test)</CardTitle>
            </div>
            <CardDescription>
              Test real-time currency conversion rates
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 md:grid-cols-3">
              <div className="space-y-2">
                <Label htmlFor="fromCurrency">From Currency</Label>
                <Select
                  id="fromCurrency"
                  value={testFrom}
                  onChange={(e) => setTestFrom(e.target.value)}
                >
                  {currencies.map((currency) => (
                    <option key={currency.code} value={currency.code}>
                      {currency.code}
                    </option>
                  ))}
                </Select>
              </div>

              <div className="space-y-2">
                <Label htmlFor="toCurrency">To Currency</Label>
                <Select
                  id="toCurrency"
                  value={testTo}
                  onChange={(e) => setTestTo(e.target.value)}
                >
                  {currencies.map((currency) => (
                    <option key={currency.code} value={currency.code}>
                      {currency.code}
                    </option>
                  ))}
                </Select>
              </div>

              <div className="space-y-2">
                <Label htmlFor="amount">Amount</Label>
                <Input
                  id="amount"
                  type="number"
                  value={testAmount}
                  onChange={(e) => setTestAmount(e.target.value)}
                  placeholder="100"
                />
              </div>
            </div>

            <Button onClick={handleTestConversion}>Test Conversion</Button>

            {testResult && (
              <div className="border rounded-lg p-4 bg-muted/30">
                {testResult.success ? (
                  <div className="space-y-2">
                    <div className="flex items-center space-x-2 text-success">
                      <Check className="h-5 w-5" />
                      <span className="font-semibold">Conversion Successful</span>
                    </div>
                    <div className="text-sm space-y-1">
                      <p>
                        <span className="text-muted-foreground">Original:</span>{' '}
                        <span className="font-medium">{testResult.originalAmount}</span>
                      </p>
                      <p>
                        <span className="text-muted-foreground">Converted:</span>{' '}
                        <span className="font-medium text-lg">{testResult.convertedAmount}</span>
                      </p>
                      <p>
                        <span className="text-muted-foreground">Exchange Rate:</span>{' '}
                        <span className="font-medium">{testResult.exchangeRate}</span>
                      </p>
                    </div>
                  </div>
                ) : (
                  <div className="flex items-center space-x-2 text-destructive">
                    <AlertCircle className="h-5 w-5" />
                    <span>{testResult.error || 'Conversion failed. Please try again.'}</span>
                  </div>
                )}
              </div>
            )}
          </CardContent>
        </Card>

        {/* Account Information */}
        <Card>
          <CardHeader>
            <CardTitle>Account Information</CardTitle>
            <CardDescription>Your trading journal account details</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="space-y-3 text-sm">
              <div className="flex justify-between">
                <span className="text-muted-foreground">Account Type:</span>
                <Badge variant="outline">Free Account</Badge>
              </div>
              <div className="flex justify-between">
                <span className="text-muted-foreground">Current Currency:</span>
                <span className="font-medium">{selectedCurrency}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-muted-foreground">Data Sync:</span>
                <Badge variant="success">Active</Badge>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Data Load — administrators only */}
        {isAdmin && (
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <RefreshCw className="h-5 w-5" />
                TrailBlazer Data Load
              </CardTitle>
              <CardDescription>
                Manually trigger a TrailBlazer data load (runs every 12 hours automatically). Fetches economic heatmap, COT reports, MyFXBook sentiment, and computes scores.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <Button onClick={handleRunDataLoad} disabled={dataLoadLoading}>
                <RefreshCw className={`h-4 w-4 mr-2 ${dataLoadLoading ? 'animate-spin' : ''}`} />
                {dataLoadLoading ? 'Starting...' : 'Run Data Load'}
              </Button>
              {dataLoadMessage && (
                <div
                  className={`flex items-start space-x-2 p-3 rounded-lg text-sm ${
                    dataLoadMessage.type === 'success'
                      ? 'bg-success/10 text-success border border-success/20'
                      : 'bg-destructive/10 text-destructive border border-destructive/20'
                  }`}
                >
                  {dataLoadMessage.type === 'success' ? (
                    <Check className="h-5 w-5 shrink-0 mt-0.5" />
                  ) : (
                    <AlertCircle className="h-5 w-5 shrink-0 mt-0.5" />
                  )}
                  <span>{dataLoadMessage.text}</span>
                </div>
              )}
            </CardContent>
          </Card>
        )}
        </div>
      </div>
    </AppLayout>
  );
}

function getDefaultCurrencies(): Currency[] {
  return [
    { code: 'USD', name: 'US Dollar', symbol: '$' },
    { code: 'ZAR', name: 'South African Rand', symbol: 'R' },
    { code: 'EUR', name: 'Euro', symbol: '€' },
    { code: 'GBP', name: 'British Pound', symbol: '£' },
    { code: 'JPY', name: 'Japanese Yen', symbol: '¥' },
    { code: 'AUD', name: 'Australian Dollar', symbol: 'A$' },
    { code: 'CAD', name: 'Canadian Dollar', symbol: 'C$' },
    { code: 'CHF', name: 'Swiss Franc', symbol: 'CHF' },
    { code: 'CNY', name: 'Chinese Yuan', symbol: '¥' },
  ];
}







