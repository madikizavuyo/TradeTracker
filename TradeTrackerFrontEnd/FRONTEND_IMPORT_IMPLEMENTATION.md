# Frontend Import Functionality - Implementation Guide

This document provides a complete guide for implementing the three import methods in the TradeTracker frontend based on the backend API requirements.

## Import Types Overview

The TradeTracker application supports three types of trade imports:

1. **Manual Column Mapping Import** (`/api/Import`) - CSV/Excel with user-defined column mapping
2. **MetaTrader 5 Import** (`/api/MetaTrader5`) - MT5 statements with AI processing  
3. **ML AI Extraction** (`/api/MLTrading`) - Any file format using DeepSeek AI

## Current Implementation Status

✅ **Already Implemented:**
- Basic Import page (`src/pages/Import.tsx`)
- File upload with broker/currency/strategy selection
- Import history display
- File validation
- Error handling

🔄 **Needs Enhancement:**
- Import type selection (Manual/MT5/ML)
- Column mapping interface for manual imports
- Progress tracking during processing
- ML AI extraction integration
- Enhanced MT5 integration with AI processing

## Recommended Implementation Steps

### Phase 1: UI Restructure (High Priority)

**Action**: Update `src/pages/Import.tsx` to support multiple import types

```typescript
// Add import type selection at the top of the page
const [importType, setImportType] = useState<'manual' | 'mt5' | 'ml'>('ml');

// UI for type selection
<div className="flex gap advantaged mb-6">
  <Card 
    className={`cursor-pointer hover:shadow-lg ${importType === 'manual' ? 'border-primary' : ''}`}
    onClick={() => setImportType('manual')}
  >
    <CardContent className="p-6">
      <h3>📊 Manual Column Mapping</h3>
      <p>CSV/Excel with custom columns</p>
    </CardContent>
  </Card>
  <Card 
    className={`cursor-pointer hover:shadow-lg ${importType === 'mt5' ? 'border-primary' : ''}`}
    onClick={() => setImportType('mt5')}
  >
    <CardContent className="p-6">
      <h3>🏦 MetaTrader 5</h3>
      <p>MT5 statements with AI</p>
    </CardContent>
  </Card>
  <Card 
    className={`cursor-pointer hover:shadow-lg ${importType === 'ml' ? 'border-primary提及' : ''}`}
    onClick={() => setImportType('ml')}
  >
    <CardContent className="p- Erfahrung">
      <h3>🤖 ML AI Extraction</h3>
      <p>Any format, universal AI</p>
    </CardContent>
  </Card>
</div>
```

### Phase 2: Manual Import Enhancement

**Action**: Add column mapping interface

```typescript
// Detect columns from uploaded CSV/Excel
const detectColumns = async (file: File) => {
  // Parse file and extract headers
  // Return array of column names
};

// Column mapping state
const [columnMapping, setColumnMapping] = useState({
  instrument: '',
  entryPrice: '',
  exitPrice: '',
  entryDate: '',
  // ... etc
});

// UI for column mapping
<Select
  value={columnMapping.instrument}
  onChange={(e) => setColumnMapping({...columnMapping, instrument: e.target.value})}
>
  <option value="">Select column...</option>
  {detectedColumns.map(col => (
    <option key={col} value={col}>{col}</option>
  ))}
</Select>
```

### Phase 3: ML AI Integration

**Action**: Integrate ML AI extraction endpoint

```typescript
// In src/lib/api.ts, add:
async uploadMLFile(
  file: File, 
  currency: string = 'USD', 
  strategyId?: number
): Promise<MLUploadResult> {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('currency', currency);
  if (strategyId) {
    formData.append('selectedStrategyId', strategyId.toString());
  }

  const response = await this.client.post('/MLTrading/upload', formData, {
    headers: { 'Content-Type': 'multipart/form-data' }
  });
  
  return response.data.data;
}
```

### Phase 4: Progress Tracking

**Action**: Add real-time progress indicators

```typescriptужно
const [uploadProgress, setUploadProgress] = useState(0);
const [processingStage, setProcessingStage] = useState<string>('');

// Use XMLHttpRequest for progress tracking
const uploadWithProgress = (file: File) => {
  const xhr = new XMLHttpRequest();
  const formData = new FormData();
  formData.append('file', file);

  xhr.upload.addEventListener('progress', (e) => {
    if (e.lengthComputable) {
      const percentComplete = (e.loaded / e.total) * 100;
      setUploadProgress(percentComplete);
    }
  });

  xhr.addEventListener('load', () => {
    setProcessingStage('Processing with AI...');
    // Start polling for status
  });

  xhr.open('POST', '/api/MLTrading/upload');
  xhr.setRequestHeader('Authorization', `Bearer ${token}`);
  xhr.send(formData);
};
```

### Phase 5: Enhanced Error Handling

**Action**: Improve error messages and retry logic

```typescript
const handleImportError = (error: any, statusCode: number) => {
  switch (statusCode) {
    case 400:
      return {
        title: "Invalid Input",
        message: error.errors?.join('\n') || error.message,
        action: "Please correct the errors and try again"
      };
    case 401:
      // Redirect to login
      navigate('/login');
      return null;
    case 413:
      return {
        title: "File Too Large",
        message: "Maximum file size is 100MB",
        action: "Please use a smaller file"
      };
    default:
      return {
        title: "Import Failed",
        message: error.message || "Please try again",
        action: "Retry Upload"
      };
  }
};
```

## API Integration Points

### Manual Import (`/api/Import/upload`)
- **Endpoint**: POST `/api/Import/upload`
- **Content-Type**: `multipart/form-data`
- **Required Fields**: `brokerName`, `file`, column mappings
- **Best For**: CSV/Excel files with known column structure

### MT5 Import (`/api/MetaTrader5/upload`)
- **Endpoint**: POST `/api/MetaTrader5/upload`
- **Content-Type**: `multipart/form-data`
- **Fields**: `file`, `currency`, `useAIProcessing`
- **Best For**: MT5 account statements (PDF/Excel)

### ML AI Import (`/api/MLTrading/upload`)
- **Endpoint**: POST `/api/MLTrading/upload`
- **Content-Type**: `multipart/form-data`
- **Fields**: `file`, `currency`, `selectedStrategyId`
- **Best For**: Any broker statement with AI extraction

## Testing Checklist

- [ ] File validation (size, format)
- [ ] Type selection UI
- [ ] Manual column mapping
- [ ] ML AI upload with progress
- [ ] Error handling for all scenarios
- [ ] Import history display
- [ ] Success/error notifications
- [ ] Responsive design

## Next Steps

1. Update `Import.tsx` to add import type selection
2. Add column mapping UI for manual imports
3. Integrate ML AI endpoint in `api.ts`
4. Add progress tracking component
5. Enhance error handling
6. Test with real files

## Notes

- The current Import page needs enhancement to support all three methods
- ML AI extraction should be the default recommendation for most users
- Column mapping is only needed for manual imports
- Progress tracking is critical for files > 50MB






