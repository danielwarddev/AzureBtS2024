using System.Collections.Generic;
using System.Diagnostics;
using AzureBts2024.Infrastructure3;
using Pulumi;
using Pulumi.AzureNative.Insights;
using Pulumi.AzureNative.OperationalInsights;
using Pulumi.AzureNative.OperationalInsights.Inputs;
using AzureNative = Pulumi.AzureNative;

return await Deployment.RunAsync(async () =>
{
    // To debug the infra project, uncomment this code, run pulumi up, then attach to the process in your IDE
    /*while (!Debugger.IsAttached)
    {
        await System.Threading.Tasks.Task.Delay(1000);
    }*/
    
    var resourceGroup = new AzureNative.Resources.ResourceGroup("resource-group");
    
    var account = new AzureNative.Storage.StorageAccount("account", new()
    {
        ResourceGroupName = resourceGroup.Name,
        Kind = AzureNative.Storage.Kind.StorageV2,
        Sku = new AzureNative.Storage.Inputs.SkuArgs
        {
            Name = AzureNative.Storage.SkuName.Standard_LRS,
        },
    });

    var workspace = new Workspace("workspace", new()
    {
        ResourceGroupName = resourceGroup.Name,
        RetentionInDays = 30,
        Sku = new WorkspaceSkuArgs
        {
            Name = "PerGB2018",
        },
        Features = new WorkspaceFeaturesArgs
        {
            EnableDataExport = true
        }
    });

    var appInsights = new Component("app Insights", new()
    {
        ResourceGroupName = resourceGroup.Name,
        ApplicationType = "web",
        Kind = "web",
        WorkspaceResourceId = workspace.Id
    });
    
    var myFunction = new StaticWebsiteFunction("myFunction", new()
    {
        ResourceGroupName = resourceGroup.Name,
        StorageAccountName = account.Name,
        SitePath = "../AzureBts2024.App/www",
        IndexDocumentName = "index.html",
        ErrorDocumentName = "error.html",
        AppInsightsInstrumentationKey = appInsights.InstrumentationKey,
        NetFrameworkVersion = "v6.0",
        AppPath = "../AzureBts2024.App"
    });
    
    return new Dictionary<string, object?>
    {
        ["siteURL"] = account.PrimaryEndpoints.Apply(primaryEndpoints => primaryEndpoints.Web),
        ["apiURL"] = myFunction.WebApp.DefaultHostName.Apply(defaultHostName => $"https://{defaultHostName}/api"),
    };
});
