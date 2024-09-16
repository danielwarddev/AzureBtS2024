using System.IO;
using System.Text.Json;
using Pulumi;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Pulumi.Command.Local;
using Pulumi.SyncedFolder;

namespace AzureBts2024.Infrastructure3;

public class StaticWebsiteFunctionArgs : ResourceArgs
{
    [Input("ResourceGroupName", true)] public Input<string> ResourceGroupName { get; set; } = null!;
    [Input("StorageAccountName", true)] public Input<string> StorageAccountName { get; set; } = null!;
    [Input("SitePath", true)] public Input<string> SitePath { get; set; } = null!;
    [Input("IndexDocumentName", true)] public Input<string> IndexDocumentName { get; set; } = null!;
    [Input("ErrorDocumentName", true)] public Input<string> ErrorDocumentName { get; set; } = null!;
    [Input("AppInsightsInstrumentationKey", true)] public Input<string> AppInsightsInstrumentationKey { get; set; } = null!;
    [Input("NetFrameworkVersion", true)] public Input<string> NetFrameworkVersion { get; set; } = "v6.0";
    [Input("AppPath", true)] public string AppPath { get; set; } = null!;
}

public class StaticWebsiteFunction : ComponentResource
{
    public WebApp WebApp { get; private set; }
    
    // package:module:type
    public StaticWebsiteFunction(string name, StaticWebsiteFunctionArgs args, ComponentResourceOptions? options = null)
        : base("danielwarddev:AzureBtS2024:StaticWebsiteFunction", name, args, options)
    {
        var website = new StorageAccountStaticWebsite("website", new()
        {
            AccountName = args.StorageAccountName,
            ResourceGroupName = args.ResourceGroupName,
            IndexDocument = args.IndexDocumentName,
            Error404Document = args.ErrorDocumentName,
        }, new() { Parent = this } );

        var syncedFolder = new AzureBlobFolder("cool-app-synced-folder", new()
        {
            Path = args.SitePath,
            ResourceGroupName = args.ResourceGroupName,
            StorageAccountName = args.StorageAccountName,
            ContainerName = website.ContainerName,
        }, new() { Parent = this } );
        
        var appContainer = new BlobContainer("cool-app-container", new()
        {
            AccountName = args.StorageAccountName,
            ResourceGroupName = args.ResourceGroupName,
            PublicAccess = PublicAccess.None,
        }, new() { Parent = this } );
        
        var outputPath = "publish";
        var publishCommand = Run.Invoke(new()
        {
            Command = $"dotnet publish --output {outputPath}",
            Dir = args.AppPath,
        }, new() { Parent = this });
        
        var appBlob = new Blob("cool-app-blob", new()
        {
            AccountName = args.StorageAccountName,
            ResourceGroupName = args.ResourceGroupName,
            ContainerName = appContainer.Name,
            Source = publishCommand.Apply(_ => new FileArchive(Path.Combine(args.AppPath, outputPath)) as AssetOrArchive),
        }, new() { Parent = this });
        
        var sasToken = ListStorageAccountServiceSAS.Invoke(new()
        {
            ResourceGroupName = args.ResourceGroupName,
            AccountName = args.StorageAccountName,
            Protocols = HttpProtocol.Https,
            SharedAccessStartTime = "2022-01-01",
            SharedAccessExpiryTime = "2030-01-01",
            Resource = SignedResource.C,
            Permissions = Permissions.R,
            ContentType = "application/json",
            CacheControl = "max-age=5",
            ContentDisposition = "inline",
            ContentEncoding = "deflate",
            CanonicalizedResource = Output.Format($"/blob/{args.StorageAccountName}/{appContainer.Name}"),
        }, new(){ Parent = this })
        .Apply(result => result.ServiceSasToken);
        
        var plan = new AppServicePlan("cool-app-plan", new()
        {
            ResourceGroupName = args.ResourceGroupName,
            Sku = new SkuDescriptionArgs
            {
                Name = "Y1",
                Tier = "Dynamic",
            },
        }, new() { Parent = this } );
        
        this.WebApp = new WebApp("my-cool-app", new()
        {
            ResourceGroupName = args.ResourceGroupName,
            ServerFarmId = plan.Id,
            Kind = "FunctionApp",
            SiteConfig = new SiteConfigArgs
            {
                NetFrameworkVersion = args.NetFrameworkVersion,
                DetailedErrorLoggingEnabled = true,
                HttpLoggingEnabled = true,
                AppSettings = new[]
                {
                    new NameValuePairArgs
                    {
                        Name = "FUNCTIONS_WORKER_RUNTIME",
                        Value = "dotnet",
                    },
                    new NameValuePairArgs
                    {
                        Name = "FUNCTIONS_EXTENSION_VERSION",
                        Value = "~4",
                    },
                    new NameValuePairArgs
                    {
                        Name = "WEBSITE_RUN_FROM_PACKAGE",
                        Value = Output.Format($"https://{args.StorageAccountName}.blob.core.windows.net/{appContainer.Name}/{appBlob.Name}?{sasToken}")
                    },
                    new NameValuePairArgs
                    {
                        Name = "APPINSIGHTS_INSTRUMENTATIONKEY",
                        Value = args.AppInsightsInstrumentationKey,
                    }
                },
                Cors = new CorsSettingsArgs
                {
                    AllowedOrigins = new[]
                    {
                        "*",
                    },
                },
            },
        }, new() { Parent = this });

        var siteConfig = new Blob("config.json", new()
        {
            AccountName = args.StorageAccountName,
            ResourceGroupName = args.ResourceGroupName,
            ContainerName = website.ContainerName,
            ContentType = "application/json",
            Source = WebApp.DefaultHostName.Apply(hostname => {
                var config = JsonSerializer.Serialize(new { api = $"https://{hostname}/api" });
                return new StringAsset(config) as AssetOrArchive;
            }),
        }, new() { Parent = this });
        
        RegisterOutputs();
    }
}