# TrueCapture Kubernetes Deployment - Changes Report

**Date:** April 13, 2026  
**Version:** 1.0 (First Kubernetes Deployment)  
**Prepared By:** DevOps Team  
**Target Environment:** Kubernetes (Minikube on Windows)

---

## Executive Summary

This document outlines all changes made to the TrueCapture application codebase to enable Kubernetes deployment. The API team should incorporate these changes in future releases to maintain deployment compatibility.

---

## 1. NEW FILES ADDED

### 1.1 Kubernetes Configuration Files (Server Directory)

| File Name | Purpose | Location |
|-----------|---------|----------|
| `api-deployment.yaml` | API deployment configuration with persistent volumes | `/Server/` |
| `api-service.yaml` | API service (NodePort 30007) | `/Server/` |
| `postgres-deployment.yaml` | PostgreSQL database deployment | `/Server/` |
| `postgres-service.yaml` | PostgreSQL service (ClusterIP) | `/Server/` |
| `postgres-pvc.yaml` | Persistent Volume Claim for PostgreSQL | `/Server/` |

### 1.2 Documentation Files

| File Name | Purpose | Location |
|-----------|---------|----------|
| `DEPLOYMENT_CHANGES_REPORT.md` | This document | `/Server/` |

---

## 2. MODIFIED FILES

### 2.1 Dockerfile Changes

**File:** `Server/Dockerfile`

**Changes Made:**

1. **Changed Base Image:**
   - FROM: `mcr.microsoft.com/dotnet/aspnet:8.0-alpine`
   - TO: `mcr.microsoft.com/dotnet/aspnet:8.0` (Debian-based)
   - **Reason:** Alpine doesn't include required libraries for PostgreSQL with Kerberos

2. **Added System Dependencies:**
   ```dockerfile
   RUN apt-get update && apt-get install -y libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
   ```
   - **Reason:** Required for PostgreSQL Kerberos authentication

3. **Added Environment Variables:**
   ```dockerfile
   ENV ASPNETCORE_URLS=http://+:5000
   ENV ConnectionStrings__TrueCaptureDb="Host=host.docker.internal;Port=5432;Database=Truecapture_1004;Username=postgres;Password=postgres;"
   ENV ConnectionStrings__DatabaseProvider="PostgreSql"
   ENV Keycloak__ServerUrl="http://host.docker.internal:8080"
   ENV Storage__BatchPath="/app/storage/batches"
   ENV Storage__DocumentPath="/app/storage/documents"
   ENV Storage__TempPath="/app/storage/temp"
   ENV Storage__SamplesPath="/app/storage/samples"
   ENV Storage__LogsPath="/app/storage/logs"
   ENV Storage__TemplatesPath="/app/storage/templates"
   ```
   - **Reason:** Container-specific configuration for database and storage paths

4. **Simplified Build Process:**
   - Removed solution file dependency
   - Direct project build from Server directory

**Full Modified Dockerfile:**
```dockerfile
# =========================
# BUILD STAGE
# =========================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy csproj and restore dependencies first (for caching)
COPY *.csproj ./
RUN dotnet restore

# Copy full source
COPY . .

# Publish application
RUN dotnet publish -c Release -o /app/publish

# =========================
# RUNTIME STAGE
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

# Install required libraries for PostgreSQL with Kerberos support
RUN apt-get update && apt-get install -y libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

# Copy published output
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV ConnectionStrings__TrueCaptureDb="Host=host.docker.internal;Port=5432;Database=Truecapture_1004;Username=postgres;Password=postgres;"
ENV ConnectionStrings__DatabaseProvider="PostgreSql"
ENV Keycloak__ServerUrl="http://host.docker.internal:8080"
ENV Storage__BatchPath="/app/storage/batches"
ENV Storage__DocumentPath="/app/storage/documents"
ENV Storage__TempPath="/app/storage/temp"
ENV Storage__SamplesPath="/app/storage/samples"
ENV Storage__LogsPath="/app/storage/logs"
ENV Storage__TemplatesPath="/app/storage/templates"

# Expose port
EXPOSE 5000

# Start application
ENTRYPOINT ["dotnet", "Server.dll"]
```

---

### 2.2 Application Configuration Changes

**File:** `Server/appsettings.json`

**Changes Made:**

1. **Database Connection String:**
   - FROM: `Host=localhost;...`
   - TO: `Host=host.docker.internal;...`
   - **Reason:** Container needs to access host machine's PostgreSQL

2. **Added KeepAlive Configuration:**
   ```json
   "KeepAlive": {
     "IntervalMinutes": 10,
     "BaseUrl": "http://localhost:5000",
     "Enabled": false
   }
   ```
   - **Reason:** Disabled for Kubernetes (not needed, only for IIS)

**Modified Section:**
```json
{
  "ConnectionStrings": {
    "TrueCaptureDb": "Host=host.docker.internal;Port=5432;Database=Truecapture_1004;Username=postgres;Password=postgres;",
    "DatabaseProvider": "PostgreSql"
  },
  "KeepAlive": {
    "IntervalMinutes": 10,
    "BaseUrl": "http://localhost:5000",
    "Enabled": false
  }
}
```

---

### 2.3 Code Changes

**File:** `Server/Services/KeepAliveService.cs`

**Changes Made:**

Added configuration check to disable service in containerized environments:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var enabled = _configuration.GetValue<bool>("KeepAlive:Enabled", true);
    
    if (!enabled)
    {
        _logger.LogInformation("KeepAlive Service is disabled via configuration.");
        return;
    }
    
    // ... rest of the code
}
```

**Reason:** KeepAlive service is only needed for IIS, not for Kubernetes deployments.

---

**File:** `Server/Program.cs`

**Changes Made:**

Added missing repository registration:

```csharp
builder.Services.AddScoped<IEmailRepository>(sp => new EmailRepository(connectionString, databaseProvider));
```

**Location:** After line with `IReportService` registration

**Reason:** EmailRepository was being used but not registered in dependency injection.

---

## 3. DATABASE CONFIGURATION CHANGES

**Database:** `Truecapture_1004`  
**Table:** `Configuration`

**Required Updates:**

The following configuration values must be updated in the database for containerized deployment:

```sql
UPDATE Configuration SET ConfigValue = '/app/storage/batches' WHERE ConfigName = 'Batch Folder';
UPDATE Configuration SET ConfigValue = '/app/storage/documents' WHERE ConfigName = 'Document Folder';
UPDATE Configuration SET ConfigValue = '/app/storage/temp' WHERE ConfigName = 'Temp Folder';
UPDATE Configuration SET ConfigValue = '/app/storage/samples' WHERE ConfigName = 'Samples Folder';
UPDATE Configuration SET ConfigValue = '/app/storage/templates' WHERE ConfigName = 'Templates Folder';
UPDATE Configuration SET ConfigValue = '/app/storage/logs' WHERE ConfigName = 'Logs Folder';
```

**Reason:** Container uses Linux-style paths instead of Windows paths (C:\TrueCapture\...)

---

## 4. STORAGE PATH MAPPING

### 4.1 Path Configuration

| Purpose | Database Config Value (Linux) | Container Mount Path | Windows Host Path |
|---------|------------------------------|---------------------|-------------------|
| Batch Files | `/app/storage/batches` | `/app/storage/batches` | `C:\TrueCapture\ICBatches` |
| Documents | `/app/storage/documents` | `/app/storage/documents` | `C:\TrueCapture\ICDocuments` |
| Temp Files | `/app/storage/temp` | `/app/storage/temp` | `C:\TrueCapture\ICTemp` |
| Samples | `/app/storage/samples` | `/app/storage/samples` | `C:\TrueCapture\ICSamples` |
| Templates | `/app/storage/templates` | `/app/storage/templates` | `C:\TrueCapture\ICTemplates` |
| Logs | `/app/storage/logs` | `/app/storage/logs` | `C:\TrueCapture\ICLogs` |

### 4.2 Volume Mount Configuration

Configured in `api-deployment.yaml`:

```yaml
volumeMounts:
  - name: batch-storage
    mountPath: /app/storage/batches
  - name: document-storage
    mountPath: /app/storage/documents
  - name: temp-storage
    mountPath: /app/storage/temp
  - name: samples-storage
    mountPath: /app/storage/samples
  - name: logs-storage
    mountPath: /app/storage/logs
  - name: templates-storage
    mountPath: /app/storage/templates

volumes:
  - name: batch-storage
    hostPath:
      path: /truecapture/ICBatches
      type: DirectoryOrCreate
  # ... (similar for other volumes)
```

---

## 5. DEPLOYMENT ARCHITECTURE

### 5.1 Components

| Component | Deployment | Image | Port | Service Type | Replicas |
|-----------|-----------|-------|------|--------------|----------|
| API | api-deployment | true-capture-api | 5000 | NodePort (30007) | 1 |
| UI | ui-deployment | true-capture-ui | 80 | NodePort (30008) | 1 |
| Database | postgres-deployment | postgres:latest | 5432 | ClusterIP | 1 |

### 5.2 Network Configuration

- **API External Access:** `http://<minikube-ip>:30007`
- **UI External Access:** `http://<minikube-ip>:30008`
- **Database Internal:** `postgres-service:5432` (ClusterIP)
- **Swagger UI:** `http://<minikube-ip>:30007/swagger`

---

## 6. BUILD AND DEPLOYMENT COMMANDS

### 6.1 Build Docker Image

```bash
cd Server
docker build -t true-capture-api .
```

### 6.2 Load Image to Minikube

```bash
minikube image load true-capture-api
```

### 6.3 Mount Windows Folders (Required for Persistent Storage)

**Terminal 1 (Keep Running):**
```bash
minikube mount C:\TrueCapture:/truecapture
```

### 6.4 Deploy to Kubernetes

**Terminal 2:**
```bash
# Deploy PostgreSQL
kubectl apply -f postgres-deployment.yaml
kubectl apply -f postgres-service.yaml

# Deploy API
kubectl apply -f api-deployment.yaml
kubectl apply -f api-service.yaml

# Check status
kubectl get pods
kubectl get services
```

### 6.5 Access Services

```bash
# Get Minikube IP
minikube ip

# Access API
minikube service api-service

# Access UI
minikube service ui-service
```

---

## 7. CONFIGURATION FILES CONTENT

### 7.1 api-deployment.yaml

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api-deployment
spec:
  replicas: 1
  selector:
    matchLabels:
      app: api
  template:
    metadata:
      labels:
        app: api
    spec:
      containers:
      - name: api
        image: true-capture-api
        imagePullPolicy: Never
        ports:
        - containerPort: 5000
        volumeMounts:
        - name: batch-storage
          mountPath: /app/storage/batches
        - name: document-storage
          mountPath: /app/storage/documents
        - name: temp-storage
          mountPath: /app/storage/temp
        - name: samples-storage
          mountPath: /app/storage/samples
        - name: logs-storage
          mountPath: /app/storage/logs
        - name: templates-storage
          mountPath: /app/storage/templates
      volumes:
      - name: batch-storage
        hostPath:
          path: /truecapture/ICBatches
          type: DirectoryOrCreate
      - name: document-storage
        hostPath:
          path: /truecapture/ICDocuments
          type: DirectoryOrCreate
      - name: temp-storage
        hostPath:
          path: /truecapture/ICTemp
          type: DirectoryOrCreate
      - name: samples-storage
        hostPath:
          path: /truecapture/ICSamples
          type: DirectoryOrCreate
      - name: logs-storage
        hostPath:
          path: /truecapture/ICLogs
          type: DirectoryOrCreate
      - name: templates-storage
        hostPath:
          path: /truecapture/ICTemplates
          type: DirectoryOrCreate
```

### 7.2 api-service.yaml

```yaml
apiVersion: v1
kind: Service
metadata:
  name: api-service
spec:
  type: NodePort
  selector:
    app: api
  ports:
  - protocol: TCP
    port: 5000
    targetPort: 5000
    nodePort: 30007
```

### 7.3 postgres-deployment.yaml

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: postgres-deployment
spec:
  replicas: 1
  selector:
    matchLabels:
      app: postgres
  template:
    metadata:
      labels:
        app: postgres
    spec:
      containers:
      - name: postgres
        image: postgres:latest
        ports:
        - containerPort: 5432
        env:
        - name: POSTGRES_USER
          value: "postgres"
        - name: POSTGRES_PASSWORD
          value: "postgres"
        - name: POSTGRES_DB
          value: "Truecapture_1004"
```

### 7.4 postgres-service.yaml

```yaml
apiVersion: v1
kind: Service
metadata:
  name: postgres-service
spec:
  type: ClusterIP
  selector:
    app: postgres
  ports:
  - protocol: TCP
    port: 5432
    targetPort: 5432
```

### 7.5 postgres-pvc.yaml

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: postgres-pvc
spec:
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 10Gi
  storageClassName: standard
```

---

## 8. KNOWN ISSUES AND WARNINGS

### 8.1 Non-Critical Warnings

These warnings appear in logs but don't affect functionality:

1. **Database Triggers/Indexes Already Exist**
   - Appears on every restart
   - Safe to ignore - indicates database is already initialized

2. **DataProtection Keys Warning**
   - Keys stored in `/root/.aspnet/DataProtection-Keys`
   - Only affects authentication persistence across container restarts
   - Not critical for development

3. **WebRootPath Not Found**
   - `/app/wwwroot` doesn't exist
   - Expected for API-only applications

### 8.2 Critical Issues to Address

1. **PostgreSQL Data Persistence**
   - ⚠️ **Current:** Data stored inside container (lost on restart)
   - ✅ **Solution:** Implement PersistentVolumeClaim (postgres-pvc.yaml provided)

2. **Keycloak Connectivity**
   - Requires Keycloak accessible at configured URL
   - Update `Keycloak:ServerUrl` in appsettings.json if needed

---

## 9. RECOMMENDATIONS FOR API TEAM

### 9.1 Code Changes to Include in Next Release

1. **Update Dockerfile** with the modified version provided in this document
2. **Add KeepAlive.Enabled configuration** to appsettings.json
3. **Add IEmailRepository registration** in Program.cs
4. **Include all Kubernetes YAML files** in the repository under `/k8s/` or `/deployment/` folder

### 9.2 Configuration Management

1. **Environment-Specific Settings:**
   - Use environment variables for sensitive data (passwords, connection strings)
   - Consider using Kubernetes Secrets for production

2. **Storage Paths:**
   - Make storage paths configurable via environment variables
   - Support both Windows and Linux path formats

3. **Database Configuration:**
   - Document the required Configuration table updates
   - Consider migration scripts for path updates

### 9.3 Documentation

1. **Add Deployment Guide** to repository
2. **Document environment variables** and their purposes
3. **Provide troubleshooting guide** for common issues

---

## 10. TESTING CHECKLIST

Before deploying to production, verify:

- [ ] All pods are running (`kubectl get pods`)
- [ ] Services are accessible (`kubectl get services`)
- [ ] Database connection is working (check API logs)
- [ ] File storage is persisting (create batch, restart pod, verify files exist)
- [ ] Swagger UI is accessible
- [ ] UI can communicate with API
- [ ] Keycloak authentication is working (if configured)
- [ ] All background services are running (Release, OCR, AutoSeparation)

---

## 11. ROLLBACK PROCEDURE

If deployment fails:

```bash
# Delete deployments
kubectl delete deployment api-deployment
kubectl delete deployment postgres-deployment

# Delete services
kubectl delete service api-service
kubectl delete service postgres-service

# Remove Docker image
docker rmi true-capture-api

# Restore previous version
# (Follow original deployment procedure with previous code)
```

---

## 12. SUPPORT AND CONTACTS

**Deployment Team:** DevOps Team  
**API Team:** [API Team Contact]  
**Database Team:** [Database Team Contact]

---

## APPENDIX A: File Checklist

### Files to Include in Repository

```
Server/
├── Dockerfile                          ✅ MODIFIED
├── appsettings.json                    ✅ MODIFIED
├── appsettings.Development.json        ⚪ NO CHANGES
├── Program.cs                          ✅ MODIFIED
├── Services/
│   └── KeepAliveService.cs            ✅ MODIFIED
├── api-deployment.yaml                 ✅ NEW FILE
├── api-service.yaml                    ✅ NEW FILE
├── postgres-deployment.yaml            ✅ NEW FILE
├── postgres-service.yaml               ✅ NEW FILE
├── postgres-pvc.yaml                   ✅ NEW FILE
└── DEPLOYMENT_CHANGES_REPORT.md        ✅ NEW FILE (This document)
```

---

## APPENDIX B: Quick Reference Commands

```bash
# Build
docker build -t true-capture-api .

# Load to Minikube
minikube image load true-capture-api

# Deploy
kubectl apply -f postgres-deployment.yaml
kubectl apply -f postgres-service.yaml
kubectl apply -f api-deployment.yaml
kubectl apply -f api-service.yaml

# Check Status
kubectl get pods
kubectl get services
kubectl logs <pod-name>

# Access Services
minikube service api-service
minikube service ui-service

# Restart Pod
kubectl delete pod -l app=api

# Clean Up
kubectl delete deployment api-deployment postgres-deployment
kubectl delete service api-service postgres-service
```

---

**End of Report**

**Document Version:** 1.0  
**Last Updated:** April 13, 2026  
**Next Review:** After first production deployment
