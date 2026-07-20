# Azure DevOps Service Migration & Operations Guide
## Lumière Media Player & Azure Functions Backend

This guide details the complete migration of **Lumière Media Player** to **Azure DevOps Service**, establishing an enterprise hybrid setup for source control, multi-stage CI/CD pipelines, Infrastructure-as-Code (Bicep), and automated backend deployment.

---

## 1. Architecture Overview

```
+-----------------------------------------------------------------------------------+
|                              AZURE DEVOPS SERVICE                                 |
|                                                                                   |
|  +---------------------+   +---------------------------------------------------+  |
|  |  Azure DevOps Repos |   |             Azure Pipelines (Multi-Stage)       |  |
|  |  - WinUI 3 Client   |-->| 1. Build_Stage (WinUI 3 MSIX + Proxy Zip Package) |  |
|  |  - Proxy Backend    |   | 2. Deploy_Proxy_Stage (Azure Function App)        |  |
|  |  - Bicep IaC        |   +---------------------------------------------------+  |
|  +---------------------+                                                          |
+-----------------------------------------------------------------------------------+
                                        |
                                        v
                       +---------------------------------+
                       |         AZURE CLOUD             |
                       |  - Azure Function App           |
                       |  - Application Insights         |
                       |  - Azure Storage Account        |
                       +---------------------------------+
```

### Components
1. **Desktop Client**: WinUI 3 packaged desktop app (`LumiereMediaPlayer.csproj`) compiled on `windows-latest` hosted agents.
2. **Backend Proxy**: .NET isolated worker Azure Function (`LumiereProxy/LumiereProxy.csproj`) protecting service API keys.
3. **Infrastructure as Code**: Bicep template (`azure-infrastructure/main.bicep`) for automated provisioning.
4. **CI/CD Pipeline**: Multi-stage YAML pipeline (`azure-pipelines.yml`) handling build, packaging, artifact drop publishing, and deployment.

---

## 2. Step-by-step Migration Guide

### Phase 1: Repository Connection

1. Log in to [Azure DevOps Service](https://dev.azure.com).
2. Create a project named **LumiereMediaPlayer**.
3. Push your local workspace or connect your GitHub repository:
   ```powershell
   git remote add azure https://dev.azure.com/{YourOrg}/LumiereMediaPlayer/_git/LumiereMediaPlayer
   git push azure --all
   ```

---

### Phase 2: Create Azure Service Connection

To allow Azure Pipelines to deploy Bicep templates and Function Apps to your Azure Subscription:

1. Open **Project Settings** in Azure DevOps.
2. Under **Pipelines**, click **Service connections**.
3. Select **New service connection** > **Azure Resource Manager** > **Service principal (automatic)**.
4. Select your **Subscription** and **Resource Group**.
5. Set the **Service connection name** to: `Azure-ARM-ServiceConnection`.
6. Check **Grant access permission to all pipelines** and click **Save**.

---

### Phase 3: Create Azure DevOps Variable Group

1. Navigate to **Pipelines** > **Library**.
2. Click **+ Variable group** and name it `Lumiere-Pipeline-Vars`.
3. Add the following variables:

| Variable Name | Description | Example / Value | Secret? |
| --- | --- | --- | --- |
| `azureServiceConnection` | Name of your Azure Service Connection | `Azure-ARM-ServiceConnection` | No |
| `azureFunctionAppName` | Name of target Azure Function App | `lumiere-proxy-prod-xxxx` | No |
| `deployProxyToAzure` | Set to `true` to enable automatic deployment stage | `true` | No |
| `APP_TOKEN` | Custom client-proxy authorization token | `YourSecretAppToken` | **Yes** |
| `WATCHMODE_API_KEY` | Secret key for Watchmode API | `your_watchmode_key` | **Yes** |
| `TMDB_API_KEY` | Secret key for TMDB API | `your_tmdb_key` | **Yes** |

4. Click **Save**.
5. Click **Pipeline permissions** at the top of the variable group and select **Grant permission to all pipelines** (or authorize when prompted during first pipeline run).
6. In `azure-pipelines.yml`, uncomment `# - group: Lumiere-Pipeline-Vars` to enable secret injection into your deployment stage.

---

### Phase 4: Deploy Azure Infrastructure (Bicep)

You can provision all Azure resources (Storage Account, Function App, App Insights, and App Settings) using the included Bicep template:

```powershell
az deployment group create `
  --resource-group rg-lumiere-prod `
  --template-file azure-infrastructure/main.bicep `
  --parameters appToken="YourSecretAppToken" `
               watchmodeApiKey="your_watchmode_key" `
               tmdbApiKey="your_tmdb_key"
```

---

### Phase 5: Pipeline Execution & Artifact Drops

1. Navigate to **Pipelines** > **Pipelines** in Azure DevOps.
2. Click **New Pipeline** > **Azure Repos Git** (or GitHub) > **Existing Azure Pipelines YAML file**.
3. Select `azure-pipelines.yml` and click **Run**.

Upon completion, Azure Pipelines will publish three artifact drops:
- **`LumiereMediaPlayer-WinUI3`**: Compiled WinUI 3 side-load package output (`.msix` / `.appx`).
- **`LumiereProxy-Package`**: Zip package ready for Azure Function App deployment.
- **`Azure-Infrastructure`**: Bicep IaC templates.

---

## 3. Maintenance & CI/CD Best Practices

- **Branch Policies**: Configure build validation rules on `main` and `master` branches in **Project Settings > Repos > Branch Policies** requiring pipeline completion prior to merging.
- **Secret Rotation**: Rotate `APP_TOKEN` and API keys in the `Lumiere-Pipeline-Vars` variable group without modifying application source code.
