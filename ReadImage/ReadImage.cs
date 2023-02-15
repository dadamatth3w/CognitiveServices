using System;
using System.Collections.Generic;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.OData.Edm;
using System.Globalization;

namespace ComputerVisionQuickstart
{
    public class Program
    {
        private static string subscriptionKey = Environment.GetEnvironmentVariable("subscriptionKey");
        private static string endpoint = Environment.GetEnvironmentVariable("endpoint");
        private static string imageUrl = Environment.GetEnvironmentVariable("imageUrl");
        private static string serviceUrl = Environment.GetEnvironmentVariable("serviceUrl");
        private static string apiKey = Environment.GetEnvironmentVariable("apiKey");

        public static ComputerVisionClient Authenticate(string endpoint, string key)
        {
            ComputerVisionClient client =
              new ComputerVisionClient(new ApiKeyServiceClientCredentials(key))
              { Endpoint = endpoint };
            return client;
        }

        public static async void SendApi(string ocrValue, ILogger log)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            int value;
            try
            {
                string trimmed = String.Concat(ocrValue.Where(c => !Char.IsWhiteSpace(c) && Char.IsDigit(c)));
                value = int.Parse(trimmed);
                log.LogInformation($"Trimmed parsed value: {value}");
            }
            catch(Exception ex)
            {
                log.LogError($"Unable to post data to API, due to {ex.Message}");
                return;
            }

            try
            {
                ShipItMeter shipItMeter = new ShipItMeter();

                shipItMeter.MeterName = "FlowMeter_1";
                shipItMeter.Read = value;
                shipItMeter.Time = DateTime.Now.ToString();

                string shipItMeterJson = JsonConvert.SerializeObject(shipItMeter);
                log.LogInformation(shipItMeterJson);
                StringContent queryString = new StringContent(shipItMeterJson, System.Text.Encoding.UTF8, "application/json");

                var buffer = System.Text.Encoding.UTF8.GetBytes(shipItMeterJson);
                var payload = new ByteArrayContent(buffer);

                HttpClient client = new HttpClient();
                HttpResponseMessage response = new HttpResponseMessage();

                client.DefaultRequestHeaders.Add("ApiKey", apiKey);

                response = await client.PostAsync(serviceUrl, queryString);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                log.LogInformation($"Succesfully posted to API");
                return;
            }
            catch (Exception ex)
            {
                log.LogError($"Unable to post data to API, due to {ex.Message}");
                return;
            }
        }

        [FunctionName("ReadImage")]
        public static async Task ReadFileUrl([TimerTrigger("%timerInterval%"
        #if DEBUG
            ,RunOnStartup=true
        #endif
            )] TimerInfo myTimer, ILogger log)
        {
            log.LogInformation("Read file from Blob Storage");

            try
            {
                ShipItMeter shipItMeter = new ShipItMeter();

                ComputerVisionClient client = Authenticate(endpoint, subscriptionKey);

                // Read text from URL
                var textHeaders = await client.ReadAsync(imageUrl);
                // After the request, get the operation location (operation ID)
                string operationLocation = textHeaders.OperationLocation;
                Thread.Sleep(2000);

                // Retrieve the URI where the extracted text will be stored from the Operation-Location header.
                // We only need the ID and not the full URL
                const int numberOfCharsInOperationId = 36;
                string operationId = operationLocation.Substring(operationLocation.Length - numberOfCharsInOperationId);

                // Extract the text
                ReadOperationResult results;
                log.LogInformation($"Extracting text from URL file {Path.GetFileName(imageUrl)}...");
                do
                {
                    results = await client.GetReadResultAsync(Guid.Parse(operationId));
                }
                while ((results.Status == OperationStatusCodes.Running ||
                    results.Status == OperationStatusCodes.NotStarted));

                // Display the found text.
                var textUrlFileResults = results.AnalyzeResult.ReadResults;
                foreach (ReadResult page in textUrlFileResults)
                {
                    if (!(page.Lines.Count() == 0))
                    {
                        foreach (Line line in page.Lines)
                        {
                            log.LogInformation(line.Text);
                            SendApi(line.Text, log);
                        }
                    }
                    else
                    {
                        log.LogInformation($"Unable to read text from counter");
                    }
                }
            }
            catch(Exception ex)
            {
                log.LogError($"Unable to get data, due to {ex.Message}");
            }
        }
    }
}

public class ShipItMeter
{
    [JsonProperty("time")]
    public string Time { get; set; }
    [JsonProperty("read")]
    public int Read { get; set; }
    [JsonProperty("meterName")]
    public string MeterName { get; set; }
}