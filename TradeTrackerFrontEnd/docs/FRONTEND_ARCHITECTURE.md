# Cursor Prompt: Trade Tracking Application Frontend

## Project Setup
- React + TypeScript + Vite
- Tailwind CSS for styling
- React Router for navigation
- Shadcn/ui components
- Lucide React icons

## Design System

### Colors (HSL in index.css)
```css
--background: 0 0% 100%;
--foreground: 222 47% 11%;
--card: 0 0% 100%;
--card-foreground: 222 47% 11%;
--primary: 222 47% 11%;
--primary-foreground: 210 40% 98%;
--secondary: 210 40% 96%;
--secondary-foreground: 222 47% 11%;
--muted: 210 40% 96%;
--muted-foreground: 215 16% 47%;
--accent: 210 40% 96%;
--accent-foreground: 222 47% 11%;
--success: 142 76% 36%;
--success-foreground: 0 0% 100%;
--destructive: 0 84% 60%;
--destructive-foreground: 0 0% 100%;
--border: 214 32% 91%;
--input: 214 32% 91%;
--ring: 222 47% 11%;
```

### Typography
- Font: Inter (Google Fonts)
- Added to index.html: `<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">`

## Project Structure

### Pages
1. **Index.tsx** - Landing page with hero, features, stats, CTA
2. **Dashboard.tsx** - Performance cards, charts placeholders, recent trades
3. **Trades.tsx** - Trade list with search/filter, mock data
4. **TradeForm.tsx** - Add/edit trade form with validation sections
5. **Strategies.tsx** - Strategy cards with performance metrics
6. **Reports.tsx** - Report generation and analytics

### Components
1. **AppLayout.tsx** - Main layout wrapper with sidebar and header
2. **AppSidebar.tsx** - Navigation sidebar with routes

### Routes (App.tsx)
```
/ - Index (landing)
/dashboard - Dashboard
/trades - Trades list
/trades/new - Add trade
/trades/:id - Edit trade
/strategies - Strategies
/reports - Reports
```

## Key Features

### Landing Page (Index.tsx)
- Navigation bar with logo and CTA buttons
- Hero section with gradient text and action buttons
- Features grid (4 features with icons)
- Statistics section (4 stat cards)
- CTA section
- Footer

### Dashboard
- 4 performance metric cards (total trades, open trades, win rate, total P&L)
- Recent trades list with mock data
- Placeholder for charts
- Color-coded P&L values (green for positive, red for negative)

### Trades Management
- Search by symbol
- Filter by status (all/open/closed)
- Trade cards with: symbol, type badge, status badge, P&L, entry/exit, strategy, broker
- Click to navigate to trade details
- Mock data with 5 sample trades

### Trade Form
- Sections: Basic Info, Entry Details, Exit Details, Notes
- Fields: symbol, type, broker, strategy, entry/exit prices, lot size, dates, stop loss, take profit, status, notes
- Validation indicators (required fields marked with *)
- Save/Cancel actions

### Strategies
- Strategy cards in grid layout
- Active/Inactive badges
- Performance summary: trades count, win rate, total P&L
- Detailed stats: avg win/loss with icons
- View Details and Edit buttons

### Reports
- Report generation controls with date range and strategy filters
- Key performance metrics list
- Monthly breakdown with P&L
- Trading insights section

## Design Patterns

### Color Usage
- Success: Green text for wins, positive P&L
- Destructive: Red text for losses, negative P&L
- Primary: Navy blue for headings and primary actions
- Badges: Color-coded by type (Long=success, Short=destructive, Status=outline)

### Layout Patterns
- Container with mx-auto and p-6
- Cards with hover effects (hover:shadow-lg transition-shadow)
- Grid layouts for responsive design (md:grid-cols-2, md:grid-cols-3, etc.)
- Consistent spacing with space-y-6, gap-4, etc.

### Navigation
- Sidebar with Home, Trades, Strategies, Reports
- Active route highlighting
- Back navigation in forms
- Click-through from lists to detail views

### Mock Data Structure
Trades include: id, symbol, type, entryPrice, exitPrice, lotSize, pnl, status, strategy, entryDate, exitDate, broker

## Implementation Notes
- All components use semantic tokens from the design system
- Responsive design with mobile-first approach
- Consistent button and card styling
- Icons from Lucide React
- Form inputs with labels and proper spacing
- Status badges with appropriate colors
- P&L values with + prefix for positive numbers


