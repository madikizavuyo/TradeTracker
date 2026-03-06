import axios, { AxiosInstance } from 'axios';
import { Trade, Strategy, DashboardData, TradeFilters, LoginResponse, AuthCheckResponse, PredictionHistory, EmailResponse, TrailBlazerNewsItem, TrailBlazerOutlookItem } from './types';

const API_BASE_URL = (import.meta as any).env?.VITE_API_BASE_URL || 'http://localhost:5235/api';

// Log API base URL for debugging
console.log('API Base URL:', API_BASE_URL);

class ApiService {
  private client: AxiosInstance;

  constructor() {
    this.client = axios.create({
      baseURL: API_BASE_URL,
      headers: {
        'Content-Type': 'application/json',
      },
    });

    // Request interceptor to add JWT token
    this.client.interceptors.request.use(
      (config) => {
        const token = localStorage.getItem('token');
        if (token) {
          config.headers.Authorization = `Bearer ${token}`;
        }
        return config;
      },
      (error) => Promise.reject(error)
    );

    // Response interceptor for error handling and token refresh
    this.client.interceptors.response.use(
      (response) => response,
      async (error) => {
        const originalRequest = error.config;

        // Handle 401 and attempt token refresh
        if (error.response?.status === 401 && !originalRequest._retry) {
          originalRequest._retry = true;

          try {
            const token = localStorage.getItem('token');
            const { data } = await axios.post(
              `${API_BASE_URL}/auth/refresh`,
              {},
              { headers: token ? { Authorization: `Bearer ${token}` } : {} }
            ).catch(() => ({ data: null }));
            const newToken = data?.data?.token || data?.token;

            if (newToken) {
              localStorage.setItem('token', newToken);
              originalRequest.headers.Authorization = `Bearer ${newToken}`;
              return this.client(originalRequest);
            }
          } catch (refreshError) {
            // Refresh failed - clear token and redirect to login
            localStorage.removeItem('token');
            localStorage.removeItem('user');
            window.location.href = '/login';
            return Promise.reject(refreshError);
          }
        }

        return Promise.reject(error);
      }
    );
  }

  // Helper to extract data from ApiResponse wrapper
  private extractData<T>(response: any): T {
    // Check if response has ApiResponse structure
    if (response.data && typeof response.data === 'object' && 'success' in response.data) {
      const apiResponse = response.data;
      if (apiResponse.success && apiResponse.data) {
        return apiResponse.data as T;
      }
      throw new Error(apiResponse.message || 'API request failed');
    }
    // Direct data response
    return response.data as T;
  }

  // Authentication
  async login(email: string, password: string, rememberMe: boolean = false): Promise<LoginResponse> {
    const response = await this.client.post('/auth/login', { 
      email, 
      password, 
      rememberMe 
    });
    // TradeHelper API returns direct response, not wrapped
    return response.data as LoginResponse;
  }

  async checkAuth(): Promise<AuthCheckResponse> {
    const response = await this.client.get('/auth/check');
    return response.data as AuthCheckResponse;
  }

  async logout(): Promise<{ message: string }> {
    const response = await this.client.post('/auth/logout');
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    return response.data as { message: string };
  }

  // Keep register for backward compatibility (if still needed)
  async register(email: string, password: string, confirmPassword: string, firstName: string, lastName: string) {
    const response = await this.client.post('/Auth/register', {
      email,
      password,
      confirmPassword,
      firstName,
      lastName,
    });
    return this.extractData(response);
  }

  async refreshToken() {
    const response = await this.client.post('/auth/refresh');
    return this.extractData(response);
  }

  async getCurrentUser() {
    const response = await this.client.get('/Auth/me');
    return this.extractData(response);
  }

  // Dashboard
  async getDashboardData(): Promise<DashboardData> {
    const response = await this.client.get('/Dashboard');
    return this.extractData<DashboardData>(response);
  }

  // Trades
  async getTrades(filters?: TradeFilters) {
    const params = new URLSearchParams();
    if (filters) {
      Object.entries(filters).forEach(([key, value]) => {
        if (value !== undefined && value !== null && value !== '') {
          params.append(key, String(value));
        }
      });
    }
    const response = await this.client.get(`/Trades/Index?${params.toString()}`);
    // Handle different response formats
    const data = this.extractData(response);
    // If response is direct array or has trades property, return as-is
    if (Array.isArray(data)) {
      return { trades: data, totalCount: data.length, totalPages: 1 };
    }
    // If response has items (pagination format)
    if (data.items) {
      return data;
    }
    // If response has trades property
    if (data.trades) {
      return {
        trades: data.trades,
        totalCount: data.trades.length,
        totalPages: 1,
        ...data
      };
    }
    return data;
  }

  async getTradeDetails(id: number): Promise<Trade> {
    const response = await this.client.get(`/Trades/${id}`);
    return this.extractData<Trade>(response);
  }

  async createTrade(trade: Partial<Trade>, entryImages?: File[], exitImages?: File[]) {
    const formData = new FormData();
    
    Object.entries(trade).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        formData.append(key, String(value));
      }
    });

    if (entryImages) {
      entryImages.forEach((file) => formData.append('entryImages', file));
    }
    if (exitImages) {
      exitImages.forEach((file) => formData.append('exitImages', file));
    }

    const response = await this.client.post('/Trades', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
    return this.extractData(response);
  }

  async updateTrade(id: number, trade: Partial<Trade>, entryImages?: File[], exitImages?: File[]) {
    const formData = new FormData();
    
    Object.entries(trade).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        formData.append(key, String(value));
      }
    });

    if (entryImages) {
      entryImages.forEach((file) => formData.append('entryImages', file));
    }
    if (exitImages) {
      exitImages.forEach((file) => formData.append('exitImages', file));
    }

    const response = await this.client.put(`/Trades/${id}`, formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
    return this.extractData(response);
  }

  async deleteTrade(id: number) {
    return this.client.delete(`/Trades/${id}`);
  }

  // Strategies
  async getStrategies(): Promise<Strategy[]> {
    const response = await this.client.get('/Strategies');
    return this.extractData<Strategy[]>(response);
  }

  async getStrategyDetails(id: number): Promise<Strategy> {
    const response = await this.client.get(`/Strategies/${id}`);
    return this.extractData<Strategy>(response);
  }

  async createStrategy(strategy: Partial<Strategy>) {
    const response = await this.client.post('/Strategies', strategy);
    return this.extractData(response);
  }

  async updateStrategy(id: number, strategy: Partial<Strategy>) {
    const response = await this.client.put(`/Strategies/${id}`, strategy);
    return this.extractData(response);
  }

  async deleteStrategy(id: number) {
    return this.client.delete(`/Strategies/${id}`);
  }

  // Reports
  async getReportsData() {
    const response = await this.client.get('/Reports');
    return this.extractData(response);
  }

  async getPerformanceReport(startDate?: string, endDate?: string, strategyId?: number) {
    const params = new URLSearchParams();
    if (startDate) params.append('startDate', startDate);
    if (endDate) params.append('endDate', endDate);
    if (strategyId) params.append('strategyId', String(strategyId));
    
    const response = await this.client.get(`/Reports/performance?${params.toString()}`);
    return this.extractData(response);
  }

  async getChartsOverview() {
    const response = await this.client.get('/Reports/charts');
    return this.extractData(response);
  }

  async getChartData(chartType: string, startDate?: string, endDate?: string, strategyId?: number) {
    const params = new URLSearchParams();
    if (startDate) params.append('startDate', startDate);
    if (endDate) params.append('endDate', endDate);
    if (strategyId) params.append('strategyId', String(strategyId));
    
    const response = await this.client.get(`/Reports/charts/${chartType}?${params.toString()}`);
    return this.extractData(response);
  }

  // Settings
  async getSettings() {
    const response = await this.client.get('/Settings');
    return this.extractData(response);
  }

  async updateCurrency(currency: string) {
    const response = await this.client.put('/Settings/UpdateCurrency', { currency });
    return this.extractData(response);
  }

  async testCurrencyConversion(fromCurrency: string, toCurrency: string, amount: number) {
    const response = await this.client.get('/Settings/currency/test', {
      params: { fromCurrency, toCurrency, amount }
    });
    return this.extractData(response);
  }

  async getAvailableCurrencies() {
    const response = await this.client.get('/Settings/currencies');
    return this.extractData(response);
  }

  // Import
  async getImportHistory() {
    const response = await this.client.get('/Import/history');
    return this.extractData(response);
  }

  async uploadImportFile(file: File, columnMappings?: Record<string, string>) {
    const formData = new FormData();
    formData.append('file', file);
    if (columnMappings) {
      Object.entries(columnMappings).forEach(([key, value]) => {
        formData.append(key, value);
      });
    }

    const response = await this.client.post('/Import/upload', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
    return this.extractData(response);
  }

  // AI Insights
  async getAIInsights(startDate?: string, endDate?: string, strategyId?: number) {
    const params = new URLSearchParams();
    if (startDate) params.append('startDate', startDate);
    if (endDate) params.append('endDate', endDate);
    if (strategyId) params.append('strategyId', String(strategyId));
    
    const response = await this.client.get(`/AI/insights?${params.toString()}`);
    return this.extractData(response);
  }

  async getAIStatus() {
    const response = await this.client.get('/AI/status');
    return this.extractData(response);
  }

  // Export
  async exportTrades(format: 'csv' | 'excel' | 'pdf', startDate?: string, endDate?: string, strategyId?: number) {
    const params = new URLSearchParams();
    params.append('format', format);
    if (startDate) params.append('startDate', startDate);
    if (endDate) params.append('endDate', endDate);
    if (strategyId) params.append('strategyId', String(strategyId));
    
    const response = await this.client.get(`/Trades/export?${params.toString()}`, {
      responseType: 'blob'
    });
    return response.data;
  }

  // Prediction endpoints
  async getPredictionHistory(): Promise<PredictionHistory[]> {
    const response = await this.client.get('/predict/history');
    return response.data as PredictionHistory[];
  }

  async runPrediction(): Promise<string> {
    const response = await this.client.post('/predict/run');
    return response.data as string;
  }

  // TrailBlazer
  async getTrailBlazerScores() {
    const response = await this.client.get('/TrailBlazer/scores');
    return response.data;
  }

  async getTrailBlazerDetail(instrumentId: number) {
    const response = await this.client.get(`/TrailBlazer/scores/${instrumentId}`);
    return response.data;
  }

  async getTrailBlazerHistory(instrumentId: number) {
    const response = await this.client.get(`/TrailBlazer/scores/history/${instrumentId}`);
    return response.data;
  }

  async getTrailBlazerHeatmap() {
    const response = await this.client.get('/TrailBlazer/heatmap');
    return response.data;
  }

  async getTrailBlazerCOT() {
    const response = await this.client.get('/TrailBlazer/cot');
    return response.data;
  }

  /** Scrape COT from CFTC and overwrite database. Sole source: cftc.gov/dea/options/financial_lof.htm */
  async scrapeTrailBlazerCOT() {
    const response = await this.client.get('/TrailBlazer/cot/scrape');
    return response.data;
  }

  async getTrailBlazerSentiment() {
    const response = await this.client.get('/TrailBlazer/sentiment');
    return response.data;
  }

  /** Manually scrape sentiment from forexclientsentiment.com and MyFXBook. Returns live data without TrailBlazer refresh. */
  async scrapeTrailBlazerSentiment() {
    const response = await this.client.get('/TrailBlazer/sentiment/scrape');
    return response.data;
  }

  async getTrailBlazerTopSetups() {
    const response = await this.client.get('/TrailBlazer/top-setups');
    return response.data;
  }

  async refreshTrailBlazer() {
    const response = await this.client.post('/TrailBlazer/refresh');
    return response.data;
  }

  /** Poll refresh progress. Returns status, step, message, percent. */
  async getTrailBlazerRefreshStatus(): Promise<{
    status: string;
    step?: string;
    message?: string;
    current: number;
    total: number;
    percent: number;
    completedAt?: string;
    error?: string;
  }> {
    const response = await this.client.get('/TrailBlazer/refresh/status');
    return response.data;
  }

  /** AI analysis of an instrument's TrailBlazer score and underlying data. */
  async getTrailBlazerAnalysis(instrumentId: number): Promise<{ analysis: string }> {
    const response = await this.client.get(`/TrailBlazer/analysis/${instrumentId}`);
    return response.data;
  }

  /** News for an instrument from Brave/Finnhub. */
  async getTrailBlazerNews(symbol: string): Promise<TrailBlazerNewsItem[]> {
    const response = await this.client.get(`/TrailBlazer/news/${encodeURIComponent(symbol)}`);
    return response.data;
  }

  /** Market outlook/forecast snippets for an instrument from Brave web search. */
  async getTrailBlazerOutlook(symbol: string): Promise<TrailBlazerOutlookItem[]> {
    const response = await this.client.get(`/TrailBlazer/outlook/${encodeURIComponent(symbol)}`);
    return response.data;
  }

  // Email endpoints
  async sendCsv(file: File): Promise<EmailResponse> {
    const formData = new FormData();
    formData.append('file', file);

    const response = await this.client.post('/email/sendcsv', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
    return response.data as EmailResponse;
  }
}

export const api = new ApiService();

