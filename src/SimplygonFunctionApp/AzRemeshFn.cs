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

            string remeshedFile;

            using (var sg = Simplygon.Loader.InitSimplygon(out var errorCode, out var errorMessage))
            {
                if (errorCode != Simplygon.EErrorCodes.NoError)
                    return new BadRequestObjectResult($"Failed! {errorCode} - {errorMessage}");

                // Find the first .glTF file in the input and process it.
                var fileToProcess = Directory.GetFiles(zipDir, "*.glTF", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (string.IsNullOrEmpty(fileToProcess))
                {
                    const string BadInputError = ".glTF input not found in zip archive.";
                    log.LogError(BadInputError);
                    return new BadRequestObjectResult($"Failed! {BadInputError}");
                }

                remeshedFile = await RunRemeshingWithMaterialCastingAsync(sg, log, fileToProcess, ToOutputPath(outputDir, fileToProcess), data.OnScreenSize);
            }

            log.LogInformation($"Processed {remeshedFile}");

            var stream = new MemoryStream();

            using (var outputZip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                outputZip.CreateEntryFromDirectory(outputDir.FullName);
            }

            stream.Position = 0;
            log.LogInformation("Cleaning up ... ");

            // Cleanup by deleting the directories
            try
            {
                outputDir.Delete(true);
                // Directory.Delete(zipDir, true); // this likely throws an exception (dangling file pointer?) but the exception is not caught by the `catch` block
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Couldn't clean up all temporary files: Output directory exists: {outputDir.Exists}; input/zip directory exists: {Directory.Exists(zipDir)}");
            }

            return new FileStreamResult(stream, System.Net.Mime.MediaTypeNames.Application.Zip)
            {
                FileDownloadName = "remeshed.zip"
            };
        }

        private static async Task<string> RunRemeshingWithMaterialCastingAsync(Simplygon.ISimplygon sg, ILogger log, string filePath, string filePathOutput, uint onScreenSize)
        {
            log.LogInformation($"Scene Importer set file path: {filePath} and output {filePathOutput}");

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

                    // Setup and run the albedo material casting.
                    string BaseColorTextureFilePath;
                    using (Simplygon.spColorCaster sgBaseColorCaster = sg.CreateColorCaster())
                    {
                        sgBaseColorCaster.SetMappingImage(sgRemeshingProcessor.GetMappingImage());
                        sgBaseColorCaster.SetSourceMaterials(sgScene.GetMaterialTable());
                        sgBaseColorCaster.SetSourceTextures(sgScene.GetTextureTable());
                        sgBaseColorCaster.SetOutputFilePath("BasecolorTexture");

                        using (Simplygon.spColorCasterSettings sgBaseColorCasterSettings = sgBaseColorCaster.GetColorCasterSettings())
                        {
                            sgBaseColorCasterSettings.SetMaterialChannel("Basecolor");
                            sgBaseColorCasterSettings.SetOutputImageFileFormat(Simplygon.EImageOutputFormat.JPEG);
                        }

                        log.LogInformation("Processing base color...");
                        sgBaseColorCaster.RunProcessing();
                        log.LogInformation("Processing base color done.");

                        BaseColorTextureFilePath = sgBaseColorCaster.GetOutputFilePath();
                    }

                    // Setup and run the ambient occlusion material casting. 
                    string OcclusionTextureFilePath;
                    using (Simplygon.spAmbientOcclusionCaster sgOcclusionCaster = sg.CreateAmbientOcclusionCaster())
                    {
                        sgOcclusionCaster.SetMappingImage(sgRemeshingProcessor.GetMappingImage());
                        sgOcclusionCaster.SetSourceMaterials(sgScene.GetMaterialTable());
                        sgOcclusionCaster.SetSourceTextures(sgScene.GetTextureTable());
                        sgOcclusionCaster.SetOutputFilePath("OcclusionTexture");

                        using (Simplygon.spAmbientOcclusionCasterSettings sgOcclusionCasterSettings = sgOcclusionCaster.GetAmbientOcclusionCasterSettings())
                        {
                            sgOcclusionCasterSettings.SetMaterialChannel("Occlusion");
                            sgOcclusionCasterSettings.SetOutputImageFileFormat(Simplygon.EImageOutputFormat.JPEG);
                        }

                        log.LogInformation("Processing occlusion...");
                        sgOcclusionCaster.RunProcessing();
                        log.LogInformation("Processing occlusion done.");

                        OcclusionTextureFilePath = sgOcclusionCaster.GetOutputFilePath();
                    }

                    string MetalnessTextureFilePath;
                    using (Simplygon.spColorCaster sgMetalnessCaster = sg.CreateColorCaster())
                    {
                        sgMetalnessCaster.SetMappingImage(sgRemeshingProcessor.GetMappingImage());
                        sgMetalnessCaster.SetSourceMaterials(sgScene.GetMaterialTable());
                        sgMetalnessCaster.SetSourceTextures(sgScene.GetTextureTable());
                        sgMetalnessCaster.SetOutputFilePath("MetalnessTexture");

                        using (Simplygon.spColorCasterSettings sgMetalnessCasterSettings = sgMetalnessCaster.GetColorCasterSettings())
                        {
                            sgMetalnessCasterSettings.SetMaterialChannel("Metalness");
                            sgMetalnessCasterSettings.SetOutputImageFileFormat(Simplygon.EImageOutputFormat.JPEG);
                        }

                        log.LogInformation("Processing MetalnessCaster...");
                        sgMetalnessCaster.RunProcessing();
                        log.LogInformation("Processing MetalnessCaster done.");

                        MetalnessTextureFilePath = sgMetalnessCaster.GetOutputFilePath();
                    }

                    string RoughnessTextureFilePath;
                    using (Simplygon.spColorCaster sgRoughnessCaster = sg.CreateColorCaster())
                    {
                        sgRoughnessCaster.SetMappingImage(sgRemeshingProcessor.GetMappingImage());
                        sgRoughnessCaster.SetSourceMaterials(sgScene.GetMaterialTable());
                        sgRoughnessCaster.SetSourceTextures(sgScene.GetTextureTable());
                        sgRoughnessCaster.SetOutputFilePath("RoughnessTexture");

                        using (Simplygon.spColorCasterSettings sgRoughnessCasterSettings = sgRoughnessCaster.GetColorCasterSettings())
                        {
                            sgRoughnessCasterSettings.SetMaterialChannel("Roughness");
                            sgRoughnessCasterSettings.SetOutputImageFileFormat(Simplygon.EImageOutputFormat.JPEG);
                        }

                        log.LogInformation("Processing RoughnessCaster...");
                        sgRoughnessCaster.RunProcessing();
                        log.LogInformation("Processing RoughnessCaster done.");

                        RoughnessTextureFilePath = sgRoughnessCaster.GetOutputFilePath();
                    }

                    // Setup and run the normals material casting. 
                    string normalsTextureFilePath;
                    using (Simplygon.spNormalCaster sgNormalsCaster = sg.CreateNormalCaster())
                    {
                        sgNormalsCaster.SetMappingImage(sgRemeshingProcessor.GetMappingImage());
                        sgNormalsCaster.SetSourceMaterials(sgScene.GetMaterialTable());
                        sgNormalsCaster.SetSourceTextures(sgScene.GetTextureTable());
                        sgNormalsCaster.SetOutputFilePath("NormalsTexture");

                        using (Simplygon.spNormalCasterSettings sgNormalsCasterSettings = sgNormalsCaster.GetNormalCasterSettings())
                        {
                            sgNormalsCasterSettings.SetMaterialChannel("Normals");
                            sgNormalsCasterSettings.SetGenerateTangentSpaceNormals(true);
                            sgNormalsCasterSettings.SetOutputImageFileFormat(Simplygon.EImageOutputFormat.JPEG);
                        }

                        log.LogInformation("Processing NormalsCaster...");
                        sgNormalsCaster.RunProcessing();
                        log.LogInformation("Processing NormalsCaster done.");

                        normalsTextureFilePath = sgNormalsCaster.GetOutputFilePath();
                    }

                    // Update scene with new casted textures. 
                    using (Simplygon.spMaterialTable sgMaterialTable = sg.CreateMaterialTable())
                    using (Simplygon.spTextureTable sgTextureTable = sg.CreateTextureTable())
                    using (Simplygon.spMaterial sgMaterial = sg.CreateMaterial())
                    {
                        using (Simplygon.spTexture sgBaseColorTexture = sg.CreateTexture())
                        {
                            sgBaseColorTexture.SetName("Basecolor");
                            sgBaseColorTexture.SetFilePath(BaseColorTextureFilePath);
                            sgTextureTable.AddTexture(sgBaseColorTexture);

                            log.LogInformation("BaseColorTexture set.");
                        }

                        using (Simplygon.spShadingTextureNode sgBaseColorTextureShadingNode = sg.CreateShadingTextureNode())
                        {
                            sgBaseColorTextureShadingNode.SetTexCoordLevel(0);
                            sgBaseColorTextureShadingNode.SetTextureName("Basecolor");

                            sgMaterial.AddMaterialChannel("Basecolor");
                            sgMaterial.SetShadingNetwork("Basecolor", sgBaseColorTextureShadingNode);

                            log.LogInformation("BaseColorTextureShadingNode set.");
                        }

                        using (Simplygon.spTexture sgNormalsTexture = sg.CreateTexture())
                        {
                            sgNormalsTexture.SetName("Normals");
                            sgNormalsTexture.SetFilePath(normalsTextureFilePath);
                            sgTextureTable.AddTexture(sgNormalsTexture);

                            log.LogInformation("NormalsTexture set.");
                        }

                        using (Simplygon.spShadingTextureNode sgNormalsTextureShadingNode = sg.CreateShadingTextureNode())
                        {
                            sgNormalsTextureShadingNode.SetTexCoordLevel(0);
                            sgNormalsTextureShadingNode.SetTextureName("Normals");

                            sgMaterial.AddMaterialChannel("Normals");
                            sgMaterial.SetShadingNetwork("Normals", sgNormalsTextureShadingNode);

                            log.LogInformation("NormalsTextureShadingNode set.");
                        }

                        using (Simplygon.spTexture sgOcclusionTexture = sg.CreateTexture())
                        {
                            sgOcclusionTexture.SetName("Occlusion");
                            sgOcclusionTexture.SetFilePath(OcclusionTextureFilePath);
                            sgTextureTable.AddTexture(sgOcclusionTexture);

                            log.LogInformation("OcclusionTexture set.");
                        }

                        using (Simplygon.spTexture sgRoughnessTexture = sg.CreateTexture())
                        {
                            sgRoughnessTexture.SetName("Roughness");
                            sgRoughnessTexture.SetFilePath(RoughnessTextureFilePath);
                            sgTextureTable.AddTexture(sgRoughnessTexture);

                            log.LogInformation("RoughnessTexture set.");
                        }

                        using (Simplygon.spShadingTextureNode sgMetalnessTextureShadingNode = sg.CreateShadingTextureNode())
                        {
                            sgMetalnessTextureShadingNode.SetTexCoordLevel(0);
                            sgMetalnessTextureShadingNode.SetTextureName("Metalness");
                            sgMaterial.AddMaterialChannel("Metalness");
                            sgMaterial.SetShadingNetwork("Metalness", sgMetalnessTextureShadingNode);

                            log.LogInformation("MetalnessTextureShadingNode set.");
                        }

                        sgMaterialTable.AddMaterial(sgMaterial);

                        sgScene.GetTextureTable().Clear();
                        sgScene.GetMaterialTable().Clear();
                        sgScene.GetTextureTable().Copy(sgTextureTable);
                        sgScene.GetMaterialTable().Copy(sgMaterialTable);

                        log.LogInformation("Update scene with new casted textures done.");
                    }
                }

                using (Simplygon.spSceneExporter sgSceneExporter = sg.CreateSceneExporter())
                {
                    sgSceneExporter.SetScene(sgScene);
                    sgSceneExporter.SetExportFilePath(filePathOutput);

                    log.LogInformation("SceneExporter run export starting...");

                    if (!sgSceneExporter.RunExport())
                    {
                        log.LogError($"Failed to save RemeshingOutput {filePathOutput}");
                        throw new Exception("Failed to save RemeshingOutput.");
                    }

                    log.LogInformation("SceneExporter done!");

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
