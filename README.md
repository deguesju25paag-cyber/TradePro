# TradePro

**WPF + .NET 8**-ko trading plataforma bat, API zerbitzariarekin (`Zerbitzaria`) eta mahaigaineko bezeroarekin (`TradePro`). Prezioak zuzenean (CoinGecko/Binance), kontrol panela, aktiboen zerrenda, grafikoarekin merkatu‑xehetasunak eta posizio/tradeen oinarrizko kudeaketa ditu.

---

## ✅ Baldintzak

- **Windows** (WPF)
- **.NET SDK 8.x**
- Visual Studio **.NET Desktop Development** workloadarekin (gomendatua)

---

## 🚀 Nola abiarazi (azkar)

> Ireki **bi terminal** repoaren erroan.

### 1) Zerbitzaria abiarazi
```powershell
# API backend
 dotnet run --project .\Zerbitzaria\Zerbitzaria.csproj
```

### 2) Bezeroa abiarazi
```powershell
# WPF aplikazioa
 dotnet run --project .\TradePro\TradePro.csproj
```

Zerbitzaria hemen entzuten du: `http://localhost:5000`

---

## 🔑 Proba‑erabiltzailea

- **Erabiltzailea:** `admin`
- **Pasahitza:** `admin`

Lehen abiaraztean automatikoki sortzen da.

---

## 🧱 Arkitektura (goi‑mailakoa)

```
TradePro/              -> WPF bezeroa (.NET 8)
Zerbitzaria/           -> API backend (Minimal API)
Zerbitzaria/Services/  -> Background zerbitzuak (prezioak, cache)
Zerbitzaria/Hubs/      -> SignalR (eguneraketak)
```

**Bezeroa**:
- WPF ikuspegiekin: `DashboardView`, `TradeView`, `MarketDetailView`, `PortfolioView`.
- REST API kontsumoa (`/api/*`).

**Zerbitzaria**:
- Minimal API (REST)
- SQLite lokala (`zerbitzaria.db`)
- Background zerbitzuak (`PriceUpdaterService`)
- Memoria‑cachea (`MarketCache`)

---

## 📌 Funtzio nagusiak

### ✔ Dashboard
- Saldoa, aktibo nabarmenak, posizio irekiak.

### ✔ Trade
- Aktiboen zerrenda orrialdekatzearekin eta UI aberatsarekin.
- Aktiboan klik egiteak grafiko‑ikuspegia irekitzen du.

### ✔ Market Detail
- Kandela‑grafikoa (Binance)
- Prezio zuzena
- Posizioen irekiera margin eta leveragearekin

### ✔ Portfolio (garapenean)
- Posizio irekiak eta PnL erakusteko prestatuta.

---

## 🧩 Endpoint nagusiak (API)

- `POST /api/login`
- `POST /api/register`
- `GET /api/markets`
- `GET /api/users/{userId}/dashboard`
- `GET /api/users/{userId}/positions`
- `GET /api/users/{userId}/trades`
- `POST /api/users/{userId}/trades` (posizioa ireki)
- `POST /api/users/{userId}/trades/{tradeId}/close`

---

## 📈 PnL nola kalkulatzen den

- **Exposure** = `Margin * Leverage`
- **Quantity** = `Exposure / EntryPrice`
- **PnL (LONG)** = `(currentPrice - entryPrice) * quantity`
- **PnL (SHORT)** = `(entryPrice - currentPrice) * quantity`

---

## 🧠 Datuak eta biltegiratzea

- SQLite datu‑basea: `zerbitzaria.db`
- Lehen abiaraztean automatikoki sortzen da.
- Taulen akatsak badaude, ezabatu DB eta berrabiarazi zerbitzaria.

---

## 🛠 Arazo‑konponbidea

### ❗ Errorea: `SQLite Error 1: no such table`
1. Zerbitzaria gelditu.
2. `zerbitzaria.db` ezabatu.
3. Zerbitzaria berriro abiarazi.

### ❗ Prezioak ez dira agertzen
- Egiaztatu internet‑konexioa.
- CoinGecko‑k rate‑limit aplika dezake (itxaron segundo batzuk).

---

## 🧪 Garapenerako aholkuak

- **Visual Studio**-n irekitzea gomendatua.
- Gaitu breakpoints `MarketDetailView.xaml.cs`-en trade‑irekitzea arazteko.
- Zerbitzaria eta bezeroa paraleloan exekuta daitezke VS‑tik **Multiple Startup Projects** erabiliz.

---

## ✨ Hurrengo hobekuntzak (ideiak)

- PnL‑ren eguneraketa denbora errealean dashboard/portfolio‑n.
- Posizioen itxiera UI‑tik.
- Bilaketa aurreratua aktiboen zerrendan.
- Trade‑en historiaren xehetasunak.

---

## 📄 Lizentzia

Barne‑erabilera / demo. Egokitu zure beharren arabera.
