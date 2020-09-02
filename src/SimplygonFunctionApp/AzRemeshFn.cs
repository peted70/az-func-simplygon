using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Linq;
using System.Diagnostics;
using SimplygonFunctionApp.Extensions;
using SimplygonFunctionApp.Models;

/// <summary>
/// IMPORTANT: THIS IS A PROOF-OF-CONCEPT, NOT INTENTED FOR PRODUCTION USE
/// </summary>
namespace SimplygonFunctionApp
{
    public static class AzRemeshFn
    {
        public const uint DEFAULT_SCREEN_SIZE = 300;
        public const uint MIN_ONSCREEN_SIZE = 20; // Simplygon says that

        [FunctionName("Remesh")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var data = await ProcessParameters(req);

            log.LogInformation($"Input parameters = {data}");

            var zipDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); // Zip extraction creates the directory
            var outputDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

            log.LogInformation($"zip dir = {zipDir}");

            HttpResponseMessage response = null;

            using (var http = new HttpClient())
            {
                log.LogInformation($"HTTP GET = {data.InputZipUri}");

                try
                {
                    response = await http.GetAsync(data.InputZipUri);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, $"Request to {data.InputZipUri} Failed");
                }
            }

            using (var za = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read))
            {
                za.ExtractToDirectory(zipDir, true);
            }

            string[] remeshedFiles;


            using (var sg = Simplygon.Loader.InitSimplygon(out var errorCode, out var errorMessage))
            {
                if (errorCode != Simplygon.EErrorCodes.NoError)
                    return new BadRequestObjectResult($"Failed! {errorCode} - {errorMessage}");

                remeshedFiles = await Task.WhenAll(Directory.GetFiles(zipDir).Select(async (file) => await RunRemeshingAsync(sg, log, file, ToOutputPath(outputDir, file), data.OnScreenSize)).ToList());
            }

            log.LogInformation($"Processed {outputDir.GetFiles().Length} files");

            var stream = new MemoryStream();

            using (var outputZip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                outputZip.CreateEntryFromDirectory(outputDir.FullName);
            }

            stream.Position = 0;
            log.LogInformation("Cleaning up ... ");

            // Cleanup by deleting the directories
            try {
                outputDir.Delete(true);
                // Directory.Delete(zipDir, true); // this likely throws an exception (dangling file pointer?) but the exception is not caught by the `catch` block
            }
            catch(Exception ex) {
                log.LogError(ex, $"Couldn't clean up all temporary files: Output directory exists: {outputDir.Exists}; input/zip directory exists: {Directory.Exists(zipDir)}");
            }

            return new FileStreamResult(stream, System.Net.Mime.MediaTypeNames.Application.Zip)
            {
                FileDownloadName = "remeshed.zip"
            };
        }

        private static async Task<string> RunRemeshingAsync(Simplygon.ISimplygon sg, ILogger log, string filePath, string filePathOutput, uint onScreenSize)
        {
            log.LogInformation($"Scene Importer set file path: {filePath}");
            log.LogInformation($"Scene Exporter set file path: {filePathOutput}");

            using (Simplygon.spSceneImporter sgSceneImporter = sg.CreateSceneImporter())
            {
                sgSceneImporter.SetImportFilePath(filePath);

                if (!sgSceneImporter.RunImport())
                {
                    log.LogError($"Scene Importer set file path FAILED: {filePath}");
                    throw new Exception($"Failed to load {filePath}.");
                }

                Simplygon.spScene sgScene = sgSceneImporter.GetScene();

                // Create the remeshing processor. 
                using (Simplygon.spRemeshingProcessor sgRemeshingProcessor = sg.CreateRemeshingProcessor())
                {
                    sgRemeshingProcessor.SetScene(sgScene);

                    using (Simplygon.spRemeshingSettings sgRemeshingSettings = sgRemeshingProcessor.GetRemeshingSettings())
                    {
                        // Set on-screen size target for remeshing. 
                        sgRemeshingSettings.SetOnScreenSize(onScreenSize);

                    }
                    // Start the remeshing process. 
                    log.LogInformation($"Scene Remeshing Processor Run started");
                    Stopwatch stopWatch = new Stopwatch();
                    stopWatch.Start();
                    await Task.Run(sgRemeshingProcessor.RunProcessing);
                    stopWatch.Stop();

                    log.LogInformation($"Scene Remeshing Processor Run finished after {stopWatch.Elapsed} ms");

                    // Remove original materials and textures from the scene as the remeshed object has a new UV. 
                    sgScene.GetTextureTable().Clear();
                    sgScene.GetMaterialTable().Clear();

                }
                using (Simplygon.spSceneExporter sgSceneExporter = sg.CreateSceneExporter())
                {
                    sgSceneExporter.SetScene(sgScene);
                    sgSceneExporter.SetExportFilePath(filePathOutput);

                    if (!sgSceneExporter.RunExport())
                    {
                        log.LogError($"Failed to save RemeshingOutput {filePathOutput}");
                        throw new Exception("Failed to save RemeshingOutput.");
                    }
                    return filePathOutput;
                }
            }
        }

        private static async Task<PostData> ProcessParameters(HttpRequest req)
        {
            PostData data = new PostData();

            if (req.Method == "GET")
            {
                var inputZip = req.Query["InputZipUri"];
                if (!string.IsNullOrEmpty(inputZip))
                {
                    data.InputZipUri = new Uri(inputZip);
                    var _onScreenSize = req.Query["OnScreenSize"];
                    uint onScreenSize = 0;
                    data.OnScreenSize = DEFAULT_SCREEN_SIZE;
                    if (!string.IsNullOrEmpty(_onScreenSize) && uint.TryParse(_onScreenSize, out onScreenSize) && onScreenSize > MIN_ONSCREEN_SIZE)
                    {
                        data.OnScreenSize = onScreenSize;
                    }
                    else
                    {
                        data.OnScreenSize = DEFAULT_SCREEN_SIZE;
                    }
                }
            }
            else if (req.Method == "POST")
            {
                var content = await new StreamReader(req.Body).ReadToEndAsync();
                data = JsonConvert.DeserializeObject<PostData>(content);
            }

            return data;
        }

        private static string ToOutputPath(DirectoryInfo outputDir, string inputFilePath, string suffix = ".remesh.")
        {
            var newFileName = Path.ChangeExtension(inputFilePath, $"{suffix}{Path.GetExtension(inputFilePath)}");
            return Path.Combine(outputDir.FullName, Path.GetFileName(newFileName));
        }
    }
}
