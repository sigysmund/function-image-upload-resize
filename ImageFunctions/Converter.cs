// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

// Learn how to locally debug an Event Grid-triggered function:
//    https://aka.ms/AA30pjh

// Use for local testing:
//   https://{ID}.ngrok.io/runtime/webhooks/EventGrid?functionName=Thumbnail

using Azure.Storage.Blobs;
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ImageFunctions
{
    public static class Converter
    {   
        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        
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
        
        [FunctionName("Converter")]
        public static async Task Run(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.Read)] Stream input,
            ILogger log)
        {

            string[] conversionVariants = new string[]
            {
                "THUMBNAIL_128",
                "THUMBNAIL_256",
                "THUMBNAIL_512",
                "NORMAL",
                "FULL",
            };

            try
            {
                if (input != null)
                {
                    var createdEvent = JsonConvert.DeserializeObject<StorageBlobCreatedEventData>(eventGridEvent.Data.ToString());
                    var extension = Path.GetExtension(createdEvent.Url);
                    var encoder = GetEncoder(extension);

                    if (encoder != null)
                    {
                        foreach (var convName in conversionVariants)
                        {
                            var thumbnailWidth = Convert.ToInt32(Environment.GetEnvironmentVariable($"{convName}_WIDTH"));
                            var thumbContainerName = Environment.GetEnvironmentVariable($"{convName}_CONTAINER_NAME");
                            var blobServiceClient = new BlobServiceClient(BLOB_STORAGE_CONNECTION_STRING);
                            var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                            var blobName = GetBlobNameFromUrl(createdEvent.Url);

                            using (var output = new MemoryStream())
                            using (var image = Image.Load(input))
                            {
                                var divisor = (decimal) image.Width / thumbnailWidth;
                                var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                                image.Mutate(x => x.Resize(thumbnailWidth, height));
                                image.Save(output, encoder);
                                output.Position = 0;
                                await blobContainerClient.UploadBlobAsync(blobName, output);
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
