# Introduction 
Sample of Simplygon v9 (headless) running on Azure Functions (v2, dotnet core)

# Getting Started

## Required Azure resources
- Azure Functions (v2, dotnet core runtime) running on App Service Plan (SKU: P2v2)
- Azure Application Insights
- Azure Storage

## Other dependencies
- Valid Simplygon v9 License 

## Deployment and Configuration Process
- Use VS2019 Web Deploy to deploy the Function App to Azure Functions
- Via the Portal, add a new App Setting in your Function App. Name: SIMPLYGON_9_PATH Value: D:\home\site\wwwroot\Simplygon9
- Connect to the Function App using FTP and manually copy the contents of the directory /src/Simplygon9 to D:\home\site\wwwroot\Simplygon9
    - Make sure you also copy a valid License to the same directory
- Change your Function App runtime Setting > Platfform > 64-bit

## Running it
- Go to the browser and execute the HTTP Trigger Function: http://[func-app].azurewebsites.net/api/Function1
- If successfull, you can download the output file via FTP from: D:\home\site\wwwroot\CZHeadSample\{NEW-GUID.fbx}
- Add your findings in this Wiki: https://dev.azure.com/zeiss-msft/VirtualTryOn/_wiki/wikis/VirtualTryOn.wiki/2/Running-Simplygon-v9-on-Azure-Functions
