---
name: fileshare-devops
description: Use when deploying, configuring Docker, setting up CI/CD, managing environment variables, or troubleshooting infrastructure
---

# Smart Share — DevOps Skill

## Overview

Smart Share runs in Docker containers. Local development uses `docker-compose` with three services (db, backend, frontend). Production deploys to Render via a GitHub Actions CI/CD pipeline that builds images, pushes to Docker Hub, and triggers Render webhooks.

**Principle:** Infrastructure as code. Everything is defined in Dockerfiles, docker-compose.yml, and GitHub Actions workflows. Nothing is configured manually in production that isn't tracked in version control.

---

## Infrastructure Map

```
LOCAL DEV                              PRODUCTION
─────────────────────                  ──────────────────────
docker-compose up                      GitHub Actions CI/CD
  │                                      │
  ├─ db (SQL Server 2022)               ├─ lint-and-test
  │    Port 1433                         ├─ build-and-push
  │                                      │   → Docker Hub
  ├─ backend (ASP.NET 8)                └─ deploy
  │    Port 5000 → container 8080           → Render webhooks
  │                                    
  └─ frontend (Nginx)                  Render Services:
       Port 5173 → container 80         ├─ Backend Web Service
                                        ├─ Frontend Web Service
                                        └─ SQL Server Database (container)
```

---

## Ports and Hostnames

| Service | Local Port | Container Port | Hostname (in docker network) |
|---|---|---|---|
| SQL Server | 1433 | 1433 | `db` |
| Backend API | 5000 | 8080 | `backend` |
| Frontend | 5173 | 80 | `frontend` |

**Critical:** Inside Docker containers, services refer to each other by service name (`db`, `backend`), NOT `localhost`.

---

## Backend Dockerfile

**Location:** `backend/Dockerfile`

```dockerfile
# ── Stage 1: Build ───────────────────────────────────────────
# Uses full SDK image with build tools (~900MB)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy .csproj files first (for dependency caching)
# Docker caches this layer — only re-runs if .csproj changes
COPY ["FileShare.API/FileShare.API.csproj", "FileShare.API/"]
COPY ["FileShare.Application/FileShare.Application.csproj", "FileShare.Application/"]
COPY ["FileShare.Domain/FileShare.Domain.csproj", "FileShare.Domain/"]
COPY ["FileShare.Infrastructure/FileShare.Infrastructure.csproj", "FileShare.Infrastructure/"]

# Restore dependencies (cached unless .csproj changed)
RUN dotnet restore "FileShare.API/FileShare.API.csproj"

# Copy all source code
COPY . .

# Publish release build
RUN dotnet publish "FileShare.API/FileShare.API.csproj" -c Release -o /app/publish

# ── Stage 2: Runtime ─────────────────────────────────────────
# Uses slim ASP.NET runtime image (~200MB)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy only compiled output from build stage
COPY --from=build /app/publish .

# ASP.NET Core listens on 8080 by default in .NET 8
EXPOSE 8080

ENTRYPOINT ["dotnet", "FileShare.API.dll"]
```

**Result:** Production image ~200MB instead of ~900MB.

---

## Frontend Dockerfile

**Location:** `frontend/Dockerfile`

```dockerfile
# ── Stage 1: Build ───────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /app

# Copy package files first (dependency caching)
COPY package*.json .
RUN npm ci

# Copy source and build
COPY . .
RUN npm run build
# Output: /app/dist/

# ── Stage 2: Runtime ─────────────────────────────────────────
FROM nginx:alpine AS runtime

# Copy built static files
COPY --from=build /app/dist /usr/share/nginx/html

# Copy nginx config for SPA routing + API proxy
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
```

---

## Nginx Configuration

**Location:** `frontend/nginx.conf`

```nginx
server {
    listen 80;
    root /usr/share/nginx/html;
    index index.html;

    # SPA fallback — all routes serve index.html
    # React Router handles client-side routing
    location / {
        try_files $uri $uri/ /index.html;
    }

    # API proxy — forward /api/ requests to backend service
    # "backend" is the docker-compose service name
    location /api/ {
        proxy_pass http://backend:8080/api/;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

**Why proxy:** Frontend and backend run on different ports. The proxy makes `/api/` requests seamless from the browser without CORS issues in production.

---

## docker-compose.yml

**Location:** project root `docker-compose.yml`

```yaml
services:
  # ── Database ──────────────────────────────────────────────
  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      SA_PASSWORD: "YourPassword123!"
      ACCEPT_EULA: "Y"
    ports:
      - "1433:1433"
    volumes:
      - sqldata:/var/opt/mssql    # Persist DB across restarts

  # ── Backend API ───────────────────────────────────────────
  backend:
    build: ./backend
    environment:
      ConnectionStrings__Default: "Server=db,1433;Database=FileShare;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True;"
      Jwt__Secret: "dev-secret-min-32-characters-here!"
      Storage__Provider: "local"
      Storage__Local__Path: "/app/uploads"
      Frontend__Url: "http://localhost:5173"
    ports:
      - "5000:8080"
    volumes:
      - uploads:/app/uploads      # Persist uploaded files
    depends_on:
      - db                        # Start DB before backend

  # ── Frontend ──────────────────────────────────────────────
  frontend:
    build: ./frontend
    ports:
      - "5173:80"
    depends_on:
      - backend                   # Start backend before frontend

volumes:
  sqldata:                        # Named volume for SQL data
  uploads:                        # Named volume for uploaded files
```

---

## .dockerignore Files

### Backend (`backend/.dockerignore`)

```
**/bin
**/obj
**/.git
**/node_modules
*.md
.vs
.vscode
```

### Frontend (`frontend/.dockerignore`)

```
node_modules
dist
.git
*.md
.vscode
```

**Why:** Exclude build artifacts, dependencies, and metadata to speed up Docker build context transfer and prevent stale files in the image.

---

## Environment Variables

### Backend Variables

| Variable | Dev (docker-compose) | Prod (Render) | Required |
|---|---|---|---|
| `ConnectionStrings__Default` | `Server=db,1433;Database=FileShare;...` | `Server=<host>,1433;Database=FileShare;...` (SQL Server) | Yes |
| `Jwt__Secret` | `dev-secret-min-32-characters-here!` | Real secret (min 32 chars) | Yes |
| `Storage__Provider` | `local` | `azure` or `s3` | Yes |
| `Storage__Local__Path` | `/app/uploads` | — | If local |
| `Storage__Azure__ConnectionString` | — | Azure connection string | If azure |
| `Frontend__Url` | `http://localhost:5173` | `https://your-app.onrender.com` | Yes |

### Frontend Variables

| Variable | Dev (.env) | Prod (build arg) | Required |
|---|---|---|---|
| `VITE_API_URL` | `http://localhost:5000/api` | `https://api.your-app.onrender.com/api` | Yes |

**Critical rules:**
- NEVER commit production secrets to git
- Dev secrets in docker-compose.yml are OK (they're local-only)
- Prod secrets go in GitHub Secrets (for CI/CD) and Render dashboard (for runtime)

### Adding a New Environment Variable (end-to-end)

```
1. Add to docker-compose.yml (backend.environment section)
2. Add to GitHub Secrets (Settings → Secrets → Actions)
3. Add to Render dashboard (service → Environment)
4. Read in code: _config["Section:Key"] or Environment.GetEnvironmentVariable()
5. Test locally with docker-compose first
6. Document in this file
```

---

## GitHub Actions CI/CD

**Location:** `.github/workflows/ci-cd.yml`

```yaml
name: CI/CD

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

env:
  REGISTRY: docker.io
  IMAGE_BACKEND: ${{ secrets.DOCKERHUB_USERNAME }}/fileshare-backend
  IMAGE_FRONTEND: ${{ secrets.DOCKERHUB_USERNAME }}/fileshare-frontend

jobs:
  # ── Phase 1: Lint & Test ────────────────────────────────────
  lint-and-test:
    name: Lint & Test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      # Backend
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.x

      - name: Restore
        run: dotnet restore backend/FileShare.sln

      - name: Format check
        run: dotnet format backend/FileShare.sln --verify-no-changes

      - name: Build
        run: dotnet build backend/FileShare.sln --no-restore

      - name: Test
        run: dotnet test backend/FileShare.sln --no-build --logger trx

      # Frontend
      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: 20

      - name: Frontend lint
        working-directory: frontend
        run: |
          npm ci
          npm run lint

  # ── Phase 2: Build & Push Docker Images ─────────────────────
  build-and-push:
    name: Build & Push
    needs: lint-and-test
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'      # Only on merge to main
    steps:
      - uses: actions/checkout@v4

      - name: Login to Docker Hub
        uses: docker/login-action@v3
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Set up Buildx
        uses: docker/setup-buildx-action@v3

      - name: Build & push backend
        uses: docker/build-push-action@v5
        with:
          context: ./backend
          push: true
          tags: ${{ env.IMAGE_BACKEND }}:latest
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Build & push frontend
        uses: docker/build-push-action@v5
        with:
          context: ./frontend
          push: true
          tags: ${{ env.IMAGE_FRONTEND }}:latest
          cache-from: type=gha
          cache-to: type=gha,mode=max

  # ── Phase 3: Deploy to Render ───────────────────────────────
  deploy:
    name: Deploy
    needs: build-and-push
    runs-on: ubuntu-latest
    steps:
      - name: Trigger backend deploy
        run: curl -X POST "${{ secrets.RENDER_DEPLOY_HOOK_BACKEND }}"

      - name: Trigger frontend deploy
        run: curl -X POST "${{ secrets.RENDER_DEPLOY_HOOK_FRONTEND }}"
```

### Pipeline Decision Tree

```
EVENT                              WHAT RUNS
├─ Push to feature branch         → lint-and-test ONLY (no build/push/deploy)
├─ Pull request to main           → lint-and-test ONLY
└─ Merge/push to main             → lint-and-test → build-and-push → deploy
```

### Required GitHub Secrets

| Secret | Where to Get It | Purpose |
|---|---|---|
| `DOCKERHUB_USERNAME` | Docker Hub account | Image registry login |
| `DOCKERHUB_TOKEN` | Docker Hub → Account Settings → Security → New Access Token | Image registry auth |
| `RENDER_DEPLOY_HOOK_BACKEND` | Render → Backend service → Settings → Deploy Hook | Trigger backend redeploy |
| `RENDER_DEPLOY_HOOK_FRONTEND` | Render → Frontend service → Settings → Deploy Hook | Trigger frontend redeploy |

---

## Render Deployment Setup

### Step 1: Create Backend Service

```
1. Render dashboard → New → Web Service
2. Select "Deploy an existing image from a registry"
3. Image URL: docker.io/<your-username>/fileshare-backend:latest
4. Name: fileshare-backend
5. Region: closest to users
6. Instance type: Free or Starter
7. Set environment variables (same as docker-compose but with prod values):
   - ConnectionStrings__Default = <SQL Server connection string>
   - Jwt__Secret = <real 32+ char secret>
   - Storage__Provider = azure (or s3)
   - Storage__Azure__ConnectionString = <azure connection string>
   - Frontend__Url = https://<frontend-service>.onrender.com
8. Save → Copy Deploy Hook URL → Add to GitHub Secrets
```

### Step 2: Create Frontend Service

```
1. New → Web Service → existing image
2. Image URL: docker.io/<your-username>/fileshare-frontend:latest
3. Name: fileshare-frontend
4. Save → Copy Deploy Hook URL → Add to GitHub Secrets
```

### Step 3: Create Database

```
1. Deploy a SQL Server 2022 container as a private service
   (or use a managed SQL Server host if available)
2. Image: mcr.microsoft.com/mssql/server:2022-latest
3. Set environment: SA_PASSWORD, ACCEPT_EULA=Y
4. Copy the internal connection string
5. Paste into backend service's ConnectionStrings__Default
```

### Step 4: Verify

```
□ Backend service is running (check Render logs)
□ Frontend loads at https://<frontend>.onrender.com
□ File upload works end-to-end
□ Database persists data across deploys
□ Deploy Hook triggers correctly from GitHub Actions
```

---

## Local Development Workflow

### First Time Setup

```bash
# 1. Clone repo
git clone <repo-url> && cd smart-share

# 2. Start all services
docker-compose up --build

# 3. Wait for all services to be healthy
#    - db: "SQL Server is now ready for client connections"
#    - backend: "Now listening on: http://[::]:8080"
#    - frontend: nginx starts silently

# 4. Apply database migrations (first time only)
docker-compose exec backend dotnet ef database update \
  --project FileShare.Infrastructure \
  --startup-project FileShare.API

# 5. Verify
#    Frontend: http://localhost:5173
#    Backend:  http://localhost:5000/api/files (should return [])
#    DB:       Connect via SSMS to localhost,1433 / sa / YourPassword123!
```

### Daily Development

```bash
# Start services (background)
docker-compose up -d

# View logs (follow)
docker-compose logs -f backend
docker-compose logs -f frontend

# Rebuild after code changes
docker-compose up --build backend    # Rebuild only backend
docker-compose up --build            # Rebuild all

# Stop all services
docker-compose down

# Stop and remove volumes (DELETES ALL DATA)
docker-compose down -v
```

### Running Commands Inside Containers

```bash
# Interactive shell in backend
docker-compose exec backend bash

# Run EF migrations
docker-compose exec backend dotnet ef migrations add NewMigration \
  --project FileShare.Infrastructure \
  --startup-project FileShare.API

# Run tests
docker-compose exec backend dotnet test FileShare.sln

# Interactive shell in frontend
docker-compose exec frontend sh

# Check database
docker-compose exec db /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "YourPassword123!" -C -Q "SELECT COUNT(*) FROM Files"
```

---

## Troubleshooting Decision Tree

```
PROBLEM: Container won't start
├─ Check logs: docker-compose logs <service>
├─ Port conflict?
│  └─ lsof -i :1433 / netstat -ano | findstr 1433
│     → Stop conflicting service or change port in docker-compose
├─ Image build failed?
│  └─ docker-compose build <service> 2>&1
│     → Fix Dockerfile or code errors
└─ Missing environment variable?
   └─ docker-compose exec <service> env | grep <VAR>

PROBLEM: Backend can't connect to database
├─ Is hostname "db" (not "localhost")?
│  └─ Connection string: Server=db,1433 (docker service name)
├─ Is SA_PASSWORD correct?
│  └─ Must match between db service and backend connection string
├─ Is DB container healthy?
│  └─ docker-compose ps (should show "Up")
└─ Is DB ready?
   └─ SQL Server takes 10-30s to initialize
      → Add healthcheck or retry logic

PROBLEM: Frontend can't reach backend
├─ Is backend running?
│  └─ docker-compose ps
├─ Is nginx.conf proxy_pass correct?
│  └─ Must be http://backend:8080/api/ (docker service name)
├─ CORS error?
│  └─ Check Frontend__Url matches http://localhost:5173
└─ API returning 500?
   └─ docker-compose logs backend

PROBLEM: GitHub Actions build fails
├─ Lint failed?
│  └─ Run locally: dotnet format --verify-no-changes
├─ Test failed?
│  └─ Run locally: dotnet test
├─ Docker push failed?
│  └─ Check DOCKERHUB_USERNAME and DOCKERHUB_TOKEN secrets
└─ Deploy webhook failed?
   └─ Check RENDER_DEPLOY_HOOK_* secrets are correct URLs

PROBLEM: Render service won't start
├─ Check Render dashboard logs
├─ Environment variables set?
│  └─ Especially ConnectionStrings__Default and Jwt__Secret
├─ Database accessible?
│  └─ Use Render's internal connection string
└─ Port correct?
   └─ Backend must EXPOSE 8080 (Render auto-detects)

PROBLEM: Files not persisting across restarts
├─ Local: Check docker-compose volumes
│  └─ uploads:/app/uploads must be defined
├─ Production: Check storage provider
│  └─ Must use azure/s3, not local (Render ephemeral filesystem)
```

---

## Build Optimization

### Docker Layer Caching Strategy

```dockerfile
# ✅ GOOD: Dependencies cached separately from code
COPY package*.json .          # Layer 1: rarely changes
RUN npm ci                    # Layer 2: cached unless package.json changed
COPY . .                      # Layer 3: changes every commit
RUN npm run build             # Layer 4: rebuilds only if code changed

# ❌ BAD: Everything in one layer
COPY . .                      # Busts cache on every change
RUN npm ci && npm run build   # Always rebuilds everything
```

### GitHub Actions Caching

The workflow uses `cache-from: type=gha` and `cache-to: type=gha,mode=max` to cache Docker layers across CI runs. This reduces build time from ~5min to ~1min for unchanged layers.

---

## Security Best Practices

| ✅ Do | ❌ Don't |
|---|---|
| Store secrets in GitHub Secrets | Commit secrets to Dockerfiles or compose |
| Use specific image tags in prod (`sdk:8.0`) | Use `latest` for base images in prod |
| Add `.dockerignore` to exclude sensitive files | Include `.git`, `node_modules` in build context |
| Use multi-stage builds | Ship SDK/build tools in production image |
| Use HTTPS in production (Render provides this) | Serve over HTTP in production |
| Rotate Docker Hub tokens periodically | Share tokens across team members |
| Run containers as non-root user | Use `--privileged` flag |
| Scan images: `docker scout cves <image>` | Skip security scanning |

---

## Monitoring and Debugging

### View Logs

```bash
# All services, follow mode
docker-compose logs -f

# Specific service, last 100 lines
docker-compose logs --tail=100 -f backend

# With timestamps
docker-compose logs -f --timestamps backend
```

### Resource Usage

```bash
# CPU, memory, network for all containers
docker stats

# Disk usage
docker system df
```

### Network Inspection

```bash
# List networks
docker network ls

# Inspect (see container IPs, connectivity)
docker network inspect smart-share_default
```

### Health Checks

```bash
# Service status
docker-compose ps

# Test backend health
curl http://localhost:5000/api/files

# Test frontend
curl http://localhost:5173
```

---

## Common Patterns

### Updating a Dockerfile

```
1. Edit the Dockerfile
2. Test locally: docker-compose build <service>
3. Test locally: docker-compose up <service>
4. Verify functionality
5. Commit + push to main
6. GitHub Actions auto-builds + deploys
```

### Rolling Back a Deployment

```
Option A: Revert code
  1. git revert <commit>
  2. Push to main
  3. CI/CD rebuilds and deploys

Option B: Manual Docker Hub tag
  1. Tag a previous good image: docker tag old-image:latest new-image:rollback
  2. Push: docker push new-image:rollback
  3. Update Render to pull specific tag
```

### Adding a New Service to docker-compose

```yaml
# Example: Adding Redis for caching
services:
  # ... existing services ...

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redisdata:/data

volumes:
  # ... existing volumes ...
  redisdata:
```

Then update `backend.depends_on` and add the connection string to environment.
