using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Amazon.S3;
using Amazon.S3.Model;

using Newtonsoft.Json;
using System.Text;

namespace HelloS3.Controllers
{
    /// <summary>
    /// ASP.NET Core controller acting as a S3 Proxy.
    /// </summary>
    [Route("api/[controller]")]
    public class S3ProxyController : Controller
    {
        IAmazonS3 S3Client { get; set; }
        ILogger Logger { get; set; }

        string BucketName { get; set; }

        public S3ProxyController(IConfiguration configuration, ILogger<S3ProxyController> logger, IAmazonS3 s3Client)
        {
            this.Logger = logger;
            this.S3Client = s3Client;

            this.BucketName = configuration[Startup.AppS3BucketKey];
            if(string.IsNullOrEmpty(this.BucketName))
            {
                logger.LogCritical("Missing configuration for S3 bucket. The AppS3Bucket configuration must be set to a S3 bucket.");
                throw new Exception("Missing configuration for S3 bucket. The AppS3Bucket configuration must be set to a S3 bucket.");
            }

            logger.LogInformation($"Configured to use bucket {this.BucketName}");
        }

        [HttpGet]
        public async Task<JsonResult> Get()
        {
            var listResponse = await this.S3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = this.BucketName
            });

            try
            {
                this.Response.ContentType = "text/json";
                //return new JsonResult(listResponse.S3Objects, new JsonSerializerSettings { Formatting = Formatting.Indented });
                List<Task> tasks = new List<Task>();
                Dictionary<string, string> bucketContents = new Dictionary<string, string>();
                List<string> keysInBucketList = listResponse.S3Objects.Select(lr => lr.Key).ToList();
                Dictionary<string, DateTime> creationDates = new Dictionary<string, DateTime>();

                // too complicated - iterate each key in the bucket; asynchronously get the item, save its last modified
                //  date so we can sort the results and get the content of the thing
                keysInBucketList.ForEach(key =>
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        // get the thing from the bucket
                        GetObjectResponse getResponse = await this.S3Client.GetObjectAsync(this.BucketName, key);

                        /// save last modified date
                        creationDates.Add(getResponse.Key, getResponse.LastModified);

                        // n.b. - encoding?
                        // sloppy
                        bucketContents.Add(getResponse.Key, await new StreamReader(getResponse.ResponseStream).ReadToEndAsync());
                    }));
                });
                await Task.WhenAll(tasks);

                // sort by last modified date, ascending
                keysInBucketList.Sort((left, right) =>
                {
                    return creationDates[left] < creationDates[right] ? -1 :
                           (creationDates[left] > creationDates[right] ? 1 : 0);
                });

                List<string> results = new List<string>();
                keysInBucketList.ForEach(key =>
                {
                    results.Add(bucketContents[key]);
                });
                return new JsonResult(new GetResult
                {
                    ResponseCode = ResponseCode.Ok,
                    Messages = results
                }, new JsonSerializerSettings { Formatting = Formatting.Indented });
            }
            catch (AmazonS3Exception e)
            {
                this.Response.StatusCode = (int)e.StatusCode;
                return new JsonResult(new GetResult
                {
                    ResponseCode = ResponseCode.AwsError,
                    Message = $"{e}"
                });
            }
            catch (Exception exception)
            {
                return new JsonResult(new GetResult
                {
                    ResponseCode = ResponseCode.Error,
                    Message = $"{exception}"
                });
            }

        }

        [HttpPut("{message}")]
        public async Task Put(string message)
        {
            string key = $"{Guid.NewGuid()}";
            // Copy the request body into a seekable stream required by the AWS SDK for .NET.
            var seekableStream = new MemoryStream();
            seekableStream.Write(Encoding.UTF8.GetBytes(message));
            seekableStream.Position = 0;

            var putRequest = new PutObjectRequest
            {
                BucketName = this.BucketName,
                Key = key,
                InputStream = seekableStream
            };

            try
            {
                var response = await this.S3Client.PutObjectAsync(putRequest);
                Logger.LogInformation($"Uploaded object {key} to bucket {this.BucketName}. Request Id: {response.ResponseMetadata.RequestId}");
            }
            catch (AmazonS3Exception e)
            {
                this.Response.StatusCode = (int)e.StatusCode;
                var writer = new StreamWriter(this.Response.Body);
                writer.Write(e.Message);
            }
        }
    }
}
