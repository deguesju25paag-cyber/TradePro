# TradePro

Una plataforma de trading **WPF + .NET 8** con servidor API (`Zerbitzaria`) y cliente de escritorio (`TradePro`). Incluye precios en vivo (CoinGecko/Binance), panel de control, lista de activos, detalle de mercado con gráfico, y gestión básica de posiciones/trades.

---

## ✅ Requisitos

- **Windows** (WPF)
- **.NET SDK 8.x**
- Visual Studio con workload **.NET Desktop Development** (recomendado)

---

## 🚀 Cómo arrancar (rápido)

> Abrir **dos terminales** en la raíz del repo.

### 1) Iniciar servidor
```powershell
# API backend
 dotnet run --project .\Zerbitzaria\Zerbitzaria.csproj
```

### 2) Iniciar cliente
```powershell
# App WPF
 dotnet run --project .\TradePro\TradePro.csproj
```

El servidor escucha en: `http://localhost:5000`

---

## 🔑 Usuario de prueba

- **Usuario:** `admin`
- **Contraseña:** `admin`

Se crea automáticamente al iniciar por primera vez.

---

## 🧱 Arquitectura (alto nivel)

```
TradePro/              -> Cliente WPF (.NET 8)
Zerbitzaria/           -> API backend (Minimal API)
Zerbitzaria/Services/  -> Background services (precios, cache)
Zerbitzaria/Hubs/      -> SignalR (actualizaciones)
```

**Cliente**:
- WPF con vistas: `DashboardView`, `TradeView`, `MarketDetailView`, `PortfolioView`.
- Consumo de API REST (`/api/*`).

**Servidor**:
- Minimal API (REST)
- SQLite local (`zerbitzaria.db`)
- Servicios en background (`PriceUpdaterService`)
- Cache en memoria (`MarketCache`)

---

## 📌 Funciones principales

### ✔ Dashboard
- Saldo, activos destacados, posiciones abiertas.

### ✔ Trade
- Listado de activos con paginación y UI rica.
- Click en activo abre la vista de gráfico.

### ✔ Market Detail
- Gráfico de velas (Binance)
- Precio en vivo
- Apertura de posiciones con margin y leverage

### ✔ Portfolio (en progreso)
- Preparado para mostrar posiciones abiertas y PnL.

---

## 🧩 Endpoints principales (API)

- `POST /api/login`
- `POST /api/register`
- `GET /api/markets`
- `GET /api/users/{userId}/dashboard`
- `GET /api/users/{userId}/positions`
- `GET /api/users/{userId}/trades`
- `POST /api/users/{userId}/trades` (abrir posición)
- `POST /api/users/{userId}/trades/{tradeId}/close`

---

## 📈 Cómo se calcula el PnL

- **Exposure** = `Margin * Leverage`
- **Quantity** = `Exposure / EntryPrice`
- **PnL (LONG)** = `(currentPrice - entryPrice) * quantity`
- **PnL (SHORT)** = `(entryPrice - currentPrice) * quantity`

---

## 🧠 Datos y almacenamiento

- Base de datos SQLite: `zerbitzaria.db`
- Se crea automáticamente en el primer arranque.
- Si hay errores de tablas, elimina la DB y vuelve a arrancar el servidor.

---

## 🛠 Solución de problemas

### ❗ Error: `SQLite Error 1: no such table`
1. Parar servidor.
2. Eliminar `zerbitzaria.db`.
3. Arrancar de nuevo el servidor.

### ❗ No aparecen precios
- Verifica conexión a internet.
- CoinGecko puede aplicar rate-limit (esperar unos segundos).

---

## 🧪 Tips para desarrollo

- Recomendado abrir en **Visual Studio**.
- Habilita breakpoints en `MarketDetailView.xaml.cs` para depurar apertura de trades.
- El servidor y cliente pueden ejecutarse en paralelo desde VS con **Multiple Startup Projects**.

---

## ✨ Próximas mejoras (ideas)

- Actualización en tiempo real de PnL en dashboard/portfolio.
- Cierre de posiciones desde UI.
- Búsqueda avanzada en la lista de activos.
- Historial detallado de trades.

---

## 📄 Licencia

Uso interno / demo. Ajusta según tus necesidades.
