# TradeTracker Frontend

A comprehensive React-based trading journal application for tracking and analyzing trading performance.

## Features

### Core Features
- **Authentication**: Secure login and registration system with session management
- **Protected Routes**: Auth-gated access to all dashboard features
- **Dashboard**: Overview of trading performance with interactive charts and key metrics
- **Trade Management**: Record, edit, and track all your trades with image uploads
- **Strategy Tracking**: Organize trades by strategy and analyze performance
- **Reports**: Generate comprehensive analytics with visual insights
- **Chart Visualizations**: Interactive equity curves, monthly performance, and strategy comparisons

### Advanced Features ⭐ NEW
- **Settings**: Currency management with 8 supported currencies and real-time conversion testing
- **Import**: CSV/Excel file uploads with automatic duplicate detection (100MB limit)
- **MT5 Integration**: Direct MetaTrader 5 connection or file uploads with AI processing
- **AI Trade Extraction**: Upload ANY document type - AI extracts trade data automatically

### Technical Features
- **Responsive Design**: Mobile-first approach with beautiful UI
- **Modern Stack**: Built with React, TypeScript, Vite, and Tailwind CSS
- **Type-Safe**: Full TypeScript coverage with strict mode
- **API Ready**: Complete integration layer for backend communication
- **State Management**: Context API for authentication and user state
- **Error Handling**: Comprehensive error handling with user-friendly messages

## Tech Stack

- **React 18** - UI library
- **TypeScript** - Type safety
- **Vite** - Build tool and dev server
- **Tailwind CSS** - Styling
- **React Router** - Navigation and protected routes
- **Axios** - API communication
- **Recharts** - Interactive chart visualizations
- **shadcn/ui** - Component library
- **Lucide React** - Icons
- **Context API** - State management

## Getting Started

### Prerequisites

- **Node.js 18+** (Required for Vite 5.x)
  - Current system has Node 16.14.2 which is unsupported
  - Upgrade from https://nodejs.org/ or use nvm
- npm or yarn

### Installation

1. Clone the repository
```bash
git clone <repository-url>
cd TradeTrackerFrontEnd
```

2. Install dependencies
```bash
npm install
```

3. Create environment file
```bash
cp .env.example .env
```

4. Update `.env` with your backend API URL
```
VITE_API_BASE_URL=http://localhost:5235/api
```

5. Start the development server
```bash
npm run dev
```

The application will be available at `http://localhost:5173`

## Available Scripts

- `npm run dev` - Start development server
- `npm run build` - Build for production
- `npm run preview` - Preview production build
- `npm run lint` - Run ESLint

## Project Structure

```
src/
├── components/           # Shared components
│   ├── ui/              # shadcn/ui components (Button, Card, Input, etc.)
│   ├── AppLayout.tsx    # Main layout wrapper
│   ├── AppSidebar.tsx   # Navigation sidebar with user profile
│   ├── ProtectedRoute.tsx # Route protection wrapper
│   ├── PerformanceChart.tsx # Line chart component
│   └── BarChartComponent.tsx # Bar chart component
├── pages/               # Route pages
│   ├── Index.tsx        # Landing page
│   ├── Login.tsx        # Login page ⭐ NEW
│   ├── Register.tsx     # Registration page ⭐ NEW
│   ├── Dashboard.tsx    # Dashboard with charts ⭐ ENHANCED
│   ├── Trades.tsx       # Trades list
│   ├── TradeForm.tsx    # Add/edit trade
│   ├── Strategies.tsx   # Strategy management
│   └── Reports.tsx      # Reports with charts ⭐ ENHANCED
├── lib/                 # Utilities and services
│   ├── api.ts           # API service layer
│   ├── types.ts         # TypeScript interfaces
│   ├── utils.ts         # Helper functions
│   └── AuthContext.tsx  # Authentication context ⭐ NEW
├── assets/              # Static assets
├── App.tsx              # Main app with protected routing ⭐ ENHANCED
├── main.tsx             # Entry point
└── index.css            # Global styles
```

## API Integration

The application connects to a backend API (ASP.NET Core). Make sure the backend is running and properly configured in your `.env` file.

See `docs/BACKEND_API.md` for complete API documentation.

## Design System

The application uses a consistent design system with:
- Custom color palette (HSL-based)
- Inter font family
- Responsive grid layouts
- Semantic component naming
- Consistent spacing and typography

See `docs/FRONTEND_ARCHITECTURE.md` for detailed design documentation.

## Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License.

## Recent Enhancements

### Authentication & Security (v2.0 - Enhanced v3.1)
See `ENHANCEMENTS.md` and `BACKEND_COMPLIANCE.md` for details:
- ✅ Complete authentication system (login/register)
- ✅ JWT Bearer token authentication
- ✅ **Two-layer token refresh** (reactive + proactive) ⭐ NEW
- ✅ Protected routes with automatic redirection
- ✅ User session management with persistence
- ✅ Password strength indicator (matches backend rules)
- ✅ Logout functionality with user display

### Visualizations & Analytics (v2.0)
- ✅ Interactive chart visualizations (Recharts)
- ✅ Equity curves and performance charts
- ✅ Monthly and strategy breakdowns
- ✅ Enhanced UI/UX with loading states

### Backend API Integration (v3.0) ⭐ NEW
See `BACKEND_INTEGRATION.md` for complete details:
- ✅ **Settings API**: 8 currencies, real-time conversion testing
- ✅ **Import API**: CSV/Excel uploads with duplicate detection
- ✅ **MetaTrader5 API**: Direct connection + file uploads + AI processing
- ✅ **ML Trading API**: AI-powered trade extraction from ANY document
- ✅ **Reports API**: Advanced analytics with filtering options
- ✅ **30+ new API methods** integrated
- ✅ **4 new pages** created (Settings, Import, MT5, ML Trading)

### Backend Compliance (v3.1 - Enhanced v3.2) 🔒 NEW
See `BACKEND_COMPLIANCE.md` for complete details:
- ✅ **JWT Authentication**: Bearer token in headers (not cookies)
- ✅ **Two-Layer Token Refresh**: Reactive (on 401) + Proactive (before expiry) ⭐ ENHANCED
- ✅ **Proactive Token Monitoring**: Auto-refresh 30 min before expiry, checks every 5 min
- ✅ **Password Validation**: Exact backend requirements (6 chars, upper, lower, digit)
- ✅ **Error Handling**: Backend error format fully supported
- ✅ **API Endpoints Updated**: `/api/Auth/*` endpoints
- ✅ **Visual Password Validator**: Real-time requirement checking

## Known Issues

### Node.js Version Compatibility
The current system is running Node.js 16.14.2, but Vite 5.x requires Node.js 18+. You'll see this error:
```
TypeError: crypto$2.getRandomValues is not a function
```

**Solution**: Upgrade Node.js to version 18 or higher:
```bash
# Option 1: Download from nodejs.org
https://nodejs.org/

# Option 2: Using nvm
nvm install 18
nvm use 18
```

After upgrading, re-install dependencies:
```bash
npm install
npm run dev
```

## Support

For issues or questions, please open an issue on GitHub.

