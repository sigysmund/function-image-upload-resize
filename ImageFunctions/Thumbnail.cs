// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

// Learn how to locally debug an Event Grid-triggered function:
//    https://aka.ms/AA30pjh

// Use for local testing:
//   https://{ID}.ngrok.io/runtime/webhooks/EventGrid?functionName=Thumbnail

using Azure.Storage.Blobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ImageFunctions
{
    public static class Thumbnail
    {
        public static readonly string[] CONVERAION_VARIANTS;
        
        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        static Thumbnail()
        {
            CONVERSION_VARIANTS = new string[]
            {
                "THUMBNAIL_128",
                "THUMBNAIL_256",
                "THUMBNAIL_512",
                "NORMAL",
                "FULL",
            };
        }
        
        private static string GetBlobNameFromUrl(string bloblUrl)
        {
            var uri = new Uri(bloblUrl);
            var blobClient = new BlobClient(uri);
            return blobClient.Name;
        }

        private static IImageEncoder GetEncoder(string extension)
        {
            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");

            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension.ToLower())
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                        encoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }

        private static bool HandleImageVariantConversion(string convName, string extension, 
                                                           StorageBlobCreatedEventData eventData, IImageEncoder encoder, 
                                                           ILogger log)
        {
            try
            {
                var thumbnailWidth = Convert.ToInt32(Environment.GetEnvironmentVariable($"{convName}_WIDTH"));
                var thumbContainerName = Environment.GetEnvironmentVariable($"{convName}_CONTAINER_NAME");
                var blobServiceClient = new BlobServiceClient(BLOB_STORAGE_CONNECTION_STRING);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                var blobName = GetBlobNameFromUrl(createdEvent.Url);

                using (var output = new MemoryStream())
                using (Image<Rgba32> image = Image.Load(input))
                {
                    var divisor = image.Width / thumbnailWidth;
                    var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                    image.Mutate(x => x.Resize(thumbnailWidth, height));
                    image.Save(output, encoder);
                    output.Position = 0;
                    await blobContainerClient.UploadBlobAsync(blobName, output);
                    return true;
                }
            }
            catch (Exception e)
            {
                log.LogInformation(ex.Message);
                return false;
            }

        }
        
        [FunctionName("Thumbnail")]
        public static async Task Run(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.Read)] Stream input,
            ILogger log)
        {
            try
            {
                if (input != null)
                {
                    var createdEvent = ((JObject)eventGridEvent.Data).ToObject<StorageBlobCreatedEventData>();
                    var extension = Path.GetExtension(createdEvent.Url);
                    var encoder = GetEncoder(extension);

                    if (encoder != null)
                    {
                        var success = false;
                        foreach (var convName in CONVERSION_VARIANTS)
                        {
                            success = HandleImageVariantConversion(convName, extension, createdEvent, encoder, log);
                            if (!success)
                            {
                                log.LogInformation($"Conversion of variant {convName} could not be handled successfully for: {createdEvent.Url}");
                            }
                        }
                    }
                    else
                    {
                        log.LogInformation($"No encoder support for: {createdEvent.Url}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }
    }
}
