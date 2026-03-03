# UT-Utah-ParceliD – Development Guide

Guide for building and running the Utah Parcel scraper in Visual Studio and deploying to Apify.

---

## 1. Create Solution in Visual Studio

### 1.1 Create New Project

1. Open **Visual Studio**.
2. **File** → **New** → **Project**.
3. Choose **Console App** (.NET).
4. Name: `UT-Utah-ParceliD`, Framework: **.NET 8.0**.

### 1.2 Add NuGet Packages

Right-click project → **Manage NuGet Packages** → **Browse** → Install:

- `Microsoft.Playwright` (e.g. 1.58.0)
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.DependencyInjection`

### 1.3 Project Structure

```
UT-Utah-ParceliD/
├── UT-Utah-ParceliD.csproj
├── Program.cs              # Entry point, input handling, orchestration
├── Models/
│   ├── InputConfig.cs      # Input (parcelId)
│   ├── UtUtahParcelRecord.cs  # Output record
│   └── InvalidParcelIdException.cs
├── Services/
│   └── UtUtahScraperService.cs  # Playwright scraping logic
├── Utils/
│   ├── ApifyHelper.cs      # Apify input/output APIs
│   └── DomHelper.cs
input.json
input_schema.json
Dockerfile
```

---

## 2. Install Playwright Browsers (Local Run)

```bash
cd UT-Utah-ParceliD
dotnet run
```

## 3. Input

- **Local:** reads `input.json`.
- **Apify:** uses `APIFY_INPUT_VALUE` (Actor input).

Example input:

```json
{"input":{"parcelId":"35:840:0124"}}
```

## 4. Docker Setup

```dockerfile
FROM mcr.microsoft.com/playwright/dotnet:v1.58.0-noble AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["UT-Utah-ParceliD/UT-Utah-ParceliD.csproj", "UT-Utah-ParceliD/"]
RUN dotnet restore "UT-Utah-ParceliD/UT-Utah-ParceliD.csproj"
COPY . .
WORKDIR "/src/UT-Utah-ParceliD"
RUN dotnet build "UT-Utah-ParceliD.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "UT-Utah-ParceliD.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UT-Utah-ParceliD.dll"]
```

## 5. Storing Data in Apify Dataset

### 5.1 On Apify Platform

When running as an Apify Actor:

- Apify sets `APIFY_DEFAULT_DATASET_ID` and `APIFY_TOKEN`.
- `ApifyHelper.PushSingleDataAsync(record)` sends each record to the Actor’s default dataset.

### 5.2 Local Run

Without Apify env vars:

- Records are written to `apify_storage/dataset/default.ndjson` (NDJSON, one JSON object per line).

### 5.3 API Usage

```csharp
await ApifyHelper.PushSingleDataAsync(record);
```

or for multiple items:

```csharp
await ApifyHelper.PushDataAsync(records);
```

---

## 6. Deploy to Apify

### 6.1 Create Actor

1. Go to [Apify Console](https://console.apify.com).
2. **Create** → **Actor**.
3. Connect Git repo or upload source.
4. Set build: **Dockerfile**.
5. Set **Input schema** from `input_schema.json`.

### 6.2 Input Schema

Use the existing `input_schema.json` in the project root.

### 6.3 Run on Apify

- Provide input, e.g. `{"parcelId": "35:840:0124, 35:840:0125"}`.
- Actor runs in headless mode; results appear in the Actor’s Dataset.

---
