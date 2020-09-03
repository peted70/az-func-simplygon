# Introduction 
Sample of Simplygon v9 (headless) running on Azure Functions (v2, dotnet core)

# Getting Started

## Deployment and Configuration Process
- TODO: add deploy to azure link here?

### Required Azure resources
- Azure Functions (v2, dotnet core runtime) running on App Service Plan
- Azure Application Insights
- Azure Blob Storage

### Other dependencies
- Valid Simplygon v9 License 

## Running it
- You can use a tool such as [curl](https://curl.haxx.se/download.html) to execute the decimation function. The example command bellow shows how to invoke the function by specifying a Zipped BLOB URI with SAS Token (containing the Exported.obj) as input and saving the output (decimated) object in the local path /tmp/out.zip:  

```sh
curl -v  -d '{"InputZipUri": "https://[BLOB ACCOUNT NAME].blob.core.windows.net/obj/[ZIPPED BLOB NAME WITH SAS TOKEN]" , "OnScreenSize": 300 }' https://[AZURE FUNCTION NAME].azurewebsites.net/api/Remesh -o /tmp/out.zip 
```
