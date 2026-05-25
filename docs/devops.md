---
name: devops
description: Dockerfile multi-stage builds and GitHub Actions CI/CD for File & Image Sharing Service. Use when writing Dockerfiles, docker-compose, or GitHub Actions workflows.
---

# DevOps — Docker & GitHub Actions

## Backend Dockerfile (multi-stage)

```dockerfile
# backend/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["FileShare.API/FileShare.API.csproj", "FileShare.API/"]
COPY ["FileShare.Application/FileShare.Application.csproj", "FileShare.Application/"]
COPY ["FileShare.Domain/FileShare.Domain.csproj", "FileShare.Domain/"]
COPY ["FileShare.Infrastructure/FileShare.Infrastructure.csproj", "FileShare.Infrastructure/"]
RUN dotnet restore "FileShare.API/FileShare.API.csproj"
COPY . .
RUN dotnet publish "FileShare.API/FileShare.API.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FileShare.API.dll"]
```

---

## Frontend Dockerfile (multi-stage)

```dockerfile
# frontend/Dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json .
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

```nginx
# frontend/nginx.conf
server {
    listen 80;
    root /usr/share/nginx/html;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;   # SPA fallback
    }

    location /api/ {
        proxy_pass http://backend:8080/api/;
        proxy_set_header Host $host;
    }
}
```

---

## docker-compose.yml (local dev)

```yaml
services:
  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      SA_PASSWORD: "YourPassword123!"
      ACCEPT_EULA: "Y"
    ports: ["1433:1433"]
    volumes:
      - sqldata:/var/opt/mssql

  backend:
    build: ./backend
    environment:
      ConnectionStrings__Default: "Server=db,1433;Database=FileShare;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True;"
      Jwt__Secret: "dev-secret-min-32-characters-here!"
      Storage__Provider: local
      Storage__Local__Path: /app/uploads
      Frontend__Url: "http://localhost:5173"
    ports: ["5000:8080"]
    volumes:
      - uploads:/app/uploads
    depends_on: [db]

  frontend:
    build: ./frontend
    ports: ["5173:80"]
    depends_on: [backend]

volumes:
  sqldata:
  uploads:
```

---

## GitHub Actions — CI/CD Pipeline

```yaml
# .github/workflows/ci-cd.yml
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
  lint-and-test:
    name: Lint & Test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.x

      - name: Restore
        run: dotnet restore backend/FileShare.sln

      - name: Format check (lint)
        run: dotnet format backend/FileShare.sln --verify-no-changes

      - name: Build
        run: dotnet build backend/FileShare.sln --no-restore

      - name: Test
        run: dotnet test backend/FileShare.sln --no-build --logger trx

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: 20

      - name: Frontend lint
        working-directory: frontend
        run: |
          npm ci
          npm run lint

  build-and-push:
    name: Build & Push Docker Images
    needs: lint-and-test
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'
    steps:
      - uses: actions/checkout@v4

      - name: Login to Docker Hub
        uses: docker/login-action@v3
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Set up Docker Buildx
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

  deploy:
    name: Deploy to Render
    needs: build-and-push
    runs-on: ubuntu-latest
    steps:
      - name: Trigger Render deploy (backend)
        run: |
          curl -X POST "${{ secrets.RENDER_DEPLOY_HOOK_BACKEND }}"

      - name: Trigger Render deploy (frontend)
        run: |
          curl -X POST "${{ secrets.RENDER_DEPLOY_HOOK_FRONTEND }}"
```

---

## GitHub Secrets to Configure

| Secret | Value |
|---|---|
| `DOCKERHUB_USERNAME` | Your Docker Hub username |
| `DOCKERHUB_TOKEN` | Docker Hub access token |
| `RENDER_DEPLOY_HOOK_BACKEND` | From Render dashboard → Deploy Hook URL |
| `RENDER_DEPLOY_HOOK_FRONTEND` | From Render dashboard → Deploy Hook URL |

---

## Render Deployment Setup

1. Create **Web Service** → connect Docker Hub image → `fileshare-backend:latest`
2. Set env vars in Render dashboard (same as docker-compose env block)
3. Create **PostgreSQL** database on Render → copy internal connection string
4. Create second **Web Service** for frontend → `fileshare-frontend:latest`
5. Copy each service's **Deploy Hook URL** → paste into GitHub Secrets

---

## .dockerignore (backend)

```
**/bin
**/obj
**/.git
**/node_modules
*.md
```

## .dockerignore (frontend)

```
node_modules
dist
.git
*.md
```
