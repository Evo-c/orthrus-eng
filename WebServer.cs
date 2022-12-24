﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HitmanPatcher
{
    internal class WebServer
    {
        [Required]
        ILoggingProvider logger;
        public WebServer(ILoggingProvider logger)
        {
            this.logger = logger;
        }

        static string GetResourceStr(string key)
        {
            ResourceManager rm = new ResourceManager("HitmanPatcher.Resources.Resource1",
                                         Assembly.GetExecutingAssembly());

            string str = Encoding.UTF8.GetString((byte[])rm.GetObject(key));
            return str;
        }

        void ProcessRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            var builder = new UriBuilder(request.Url);
            switch (request.Url.Port)
            {
                case 5866:
                    {
                        builder.Host = "config.hitman.io";
                        break;
                    }

                case 5867:
                    {
                        builder.Host = "hm3-service.hitman.io";
                        break;
                    }
            }

            builder.Port = 443;
            builder.Scheme = "https";
            HttpWebRequest destinationRequest = (HttpWebRequest)WebRequest.Create(builder.Uri);
            destinationRequest.Method = request.HttpMethod;
            destinationRequest.ContentType = request.ContentType;
            destinationRequest.ContentLength = request.ContentLength64;
            destinationRequest.UserAgent = request.UserAgent;
            destinationRequest.CookieContainer = new CookieContainer();
            destinationRequest.CookieContainer.Add(request.Cookies);
            destinationRequest.Referer = request.UrlReferrer?.ToString();
            destinationRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            foreach (var key in request.Headers.AllKeys)
            {
                try
                {
                    destinationRequest.Headers.Add(key, request.Headers[key]);
                }
                catch (Exception e)
                {

                }
            }

            bool uriLogged = false;

            Action<string> logFunctionApplied = (string fnName) =>
            {
                if (!uriLogged)
                    logger.log($"[H] ↑ {builder.Uri.PathAndQuery.ToString()}\n");
                uriLogged = true;

                logger.log($"[A] {fnName}");
            };

            Func<string, bool> applyFuncOrNot = (string fnName) =>
            {
                if (MainForm.functionEnabled[fnName])
                {
                    logFunctionApplied(fnName);
                    return true;
                }
                return false;
            };

            if (request.HasEntityBody)
            {
                using (Stream requestBodyStream = request.InputStream)
                using (Stream destinationRequestStream = destinationRequest.GetRequestStream())
                {
                    requestBodyStream.CopyTo(destinationRequestStream);
                }
            }

            try
            {
                // Get the response from the destination server
                HttpWebResponse destinationResponse = (HttpWebResponse)destinationRequest.GetResponse();
                // Send the response back to the client
                response.ContentType = destinationResponse.ContentType;
                // response.ContentLength64 = destinationResponse.ContentLength/**/;
                response.StatusCode = (int)destinationResponse.StatusCode;
                response.StatusDescription = destinationResponse.StatusDescription;
                response.Cookies = destinationResponse.Cookies;
                foreach (var key in destinationResponse.Headers.AllKeys)
                {
                    try
                    {
                        response.Headers.Add(key, destinationResponse.Headers[key]);
                    }
                    catch (Exception e)
                    {

                    }
                }

                // Read the response body into memory
                MemoryStream responseBodyStream = new MemoryStream();
                using (Stream destinationResponseStream = destinationResponse.GetResponseStream())
                {
                    destinationResponseStream.CopyTo(responseBodyStream);
                }
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                byte[] responseBody = responseBodyStream.ToArray();

                if (request.Url.PathAndQuery.StartsWith("/config/pc-prod"))
                {
                    if (applyFuncOrNot("basic.hook-service-addr"))
                    {
                        string responseBodyString = Encoding.UTF8.GetString(responseBody);
                        responseBodyString = responseBodyString.Replace("https://hm3-service.hitman.io", "http://localhost:5867");
                        responseBody = Encoding.UTF8.GetBytes(responseBodyString);
                    }
                }


                if (request.Url.PathAndQuery.StartsWith("/profiles/page/Stashpoint"))
                {
                    string responseBodyString = Encoding.UTF8.GetString(responseBody);
                    JObject obj = JsonConvert.DeserializeObject<JObject>(responseBodyString);

                    if (!request.Url.Query.Contains("disguise") && applyFuncOrNot("items.add-unlockables-stashpoint"))
                    {
                        var itemsData = new List<JToken>();
                        var unlockables = JsonConvert.DeserializeObject<List<JObject>>(GetResourceStr("allunlockables"));
                        JObject template = JsonConvert.DeserializeObject<JObject>(GetResourceStr("loadoutUnlockableTemplate.json"));
                        foreach (var unlockable in unlockables)
                        {
                            if (!"weapon,gear".Contains((string)unlockable["Type"])) continue;

                            JToken checkout = template.DeepClone();
                            checkout["Item"]["InstanceId"] = Guid.NewGuid();
                            checkout["Item"]["ProfileId"] = Guid.NewGuid();
                            checkout["Item"]["Unlockable"]["Properties"] = unlockable["Properties"];
                            checkout["Item"]["Unlockable"]["Id"] = unlockable["Id"];
                            checkout["Item"]["Unlockable"]["Guid"] = unlockable["Guid"];
                            checkout["Item"]["Unlockable"]["Type"] = unlockable["Type"];
                            checkout["Item"]["Unlockable"]["Subtype"] = unlockable["Subtype"];
                            checkout["SlotId"] = obj["data"]["SlotId"];

                            itemsData.Add(checkout);
                        }

                        obj["data"]["LoadoutItemsData"]["Items"] = JToken.FromObject(itemsData);
                    }

                    if (!request.Url.Query.Contains("disguise") && applyFuncOrNot("items.add-unlockables-num"))
                    {

                        foreach (var item in obj["data"]["LoadoutItemsData"]["Items"])
                        {
                            var id = (string)item["Item"]["Unlockable"]["Properties"]["RepositoryId"];
                            var list = Enumerable.Repeat(id, 100).ToList();
                            item["Item"]["Unlockable"]["Properties"]["RepositoryAssets"] = JToken.FromObject(list);
                        }
                    }

                    if (!request.Url.Query.Contains("disguise") && applyFuncOrNot("items.all-items"))
                    {
                        var ids = new List<JToken>();
                        foreach (var item in obj["data"]["LoadoutItemsData"]["Items"])
                        {
                            if (!"weapon,gear".Contains((string)item["Item"]["Unlockable"]["Type"])) continue;
                            ids.Add(item["Item"]["Unlockable"]["Properties"]["RepositoryId"]);
                        }

                        {
                            JObject template = JsonConvert.DeserializeObject<JObject>(GetResourceStr("loadoutUnlockableTemplate.json"));

                            Random random = new Random();
                            int value = random.Next(0, ids.Count);
                            var ico = ids[value];
                            template["Item"]["Unlockable"]["Properties"]["RepositoryId"] = ico;
                            template["Item"]["Unlockable"]["Guid"] = Guid.NewGuid();

                            template["Item"]["Unlockable"]["Properties"]["RepositoryAssets"] = JToken.FromObject(ids);
                            template["Item"]["InstanceId"] = Guid.NewGuid();
                            template["Item"]["ProfileId"] = Guid.NewGuid();
                            template["Item"]["Unlockable"]["Type"] = "All Items";
                            template["Item"]["Unlockable"]["Subtype"] = "All Items";
                            template["Item"]["Unlockable"]["Id"] = "All Items";
                            template["Item"]["Unlockable"]["Properties"]["Name"] = "All Items";
                            template["Item"]["Unlockable"]["Properties"]["Description"] = "选这个就可以获得可以共存的大部分道具每样一个\n\nOrthrus\nMicroBlock";
                            template["Item"]["Unlockable"]["Properties"]["Id"] = "All Items";
                            template["SlotId"] = obj["data"]["SlotId"];

                            obj["data"]["LoadoutItemsData"]["Items"] = JToken.FromObject(
                                obj["data"]["LoadoutItemsData"]["Items"].Prepend(template)
                                );
                        }
                    }

                    if (request.Url.Query.Contains("disguise") && applyFuncOrNot("items.add-unlockables-disguise"))
                    {
                        var itemsData = new List<JToken>();
                        var unlockables = JsonConvert.DeserializeObject<List<JObject>>(GetResourceStr("allunlockables"));
                        JObject template = JsonConvert.DeserializeObject<JObject>(GetResourceStr("loadoutUnlockableTemplate.json"));
                        foreach (var unlockable in unlockables)
                        {
                            if (!"disguise".Contains((string)unlockable["Type"])) continue;

                            JToken checkout = template.DeepClone();
                            checkout["Item"]["InstanceId"] = Guid.NewGuid();
                            checkout["Item"]["ProfileId"] = Guid.NewGuid();
                            checkout["Item"]["Unlockable"]["Properties"] = unlockable["Properties"];
                            checkout["Item"]["Unlockable"]["Id"] = unlockable["Id"];
                            checkout["Item"]["Unlockable"]["Guid"] = unlockable["Guid"];
                            checkout["Item"]["Unlockable"]["Type"] = unlockable["Type"];
                            checkout["Item"]["Unlockable"]["Subtype"] = unlockable["Subtype"];
                            checkout["SlotId"] = obj["data"]["SlotId"];

                            itemsData.Add(checkout);
                        }

                        obj["data"]["LoadoutItemsData"]["Items"] = JToken.FromObject(itemsData);
                    }

                    string modifiedJson = JsonConvert.SerializeObject(obj);
                    responseBody = Encoding.UTF8.GetBytes(modifiedJson);
                }

                if (request.Url.PathAndQuery.StartsWith("/profiles/page/SelectEntrance"))
                {
                    string responseBodyString = Encoding.UTF8.GetString(responseBody);
                    JObject obj = JsonConvert.DeserializeObject<JObject>(responseBodyString);

                    if (applyFuncOrNot("entrance.unlock-all"))
                    {
                        foreach (var entrance in obj["data"]["OrderedUnlocks"])
                        {
                            JObject entranceProperties = (JObject)entrance["Properties"];

                            if (!entranceProperties.ContainsKey("UnlockOrder"))
                            {
                                entranceProperties.Add("UnlockOrder", 0);
                            }
                            else
                            {
                                entranceProperties["UnlockOrder"] = 0;
                            }

                            if (!entranceProperties.ContainsKey("UnlockLevel"))
                            {
                                entranceProperties.Add("UnlockLevel", "-1");
                            }
                            else
                            {
                                entranceProperties["UnlockLevel"] = "-1";
                            }

                            if (!entranceProperties.ContainsKey("UnlockedByDefault"))
                            {
                                entranceProperties.Add("UnlockedByDefault", true);
                            }
                            else
                            {
                                entranceProperties["UnlockedByDefault"] = true;
                            }

                            JObject entranceLoadoutSettings = (JObject)entranceProperties["LoadoutSettings"];
                            if (entranceLoadoutSettings == null) entranceLoadoutSettings = new JObject();

                            if (!entranceLoadoutSettings.ContainsKey("GearSlotsEnabledCount"))
                            {
                                entranceLoadoutSettings.Add("GearSlotsEnabledCount", 999);
                            }
                            else
                            {
                                entranceLoadoutSettings["GearSlotsEnabledCount"] = 999;
                            }

                            if (!entranceLoadoutSettings.ContainsKey("GearSlotsAllowContainers"))
                            {
                                entranceLoadoutSettings.Add("GearSlotsAllowContainers", true);
                            }
                            else
                            {
                                entranceLoadoutSettings["GearSlotsAllowContainers"] = true;
                            }

                            if (!entranceLoadoutSettings.ContainsKey("ConcealedWeaponSlotEnabled"))
                            {
                                entranceLoadoutSettings.Add("ConcealedWeaponSlotEnabled", true);
                            }
                            else
                            {
                                entranceLoadoutSettings["ConcealedWeaponSlotEnabled"] = true;
                            }
                        }
                    }

                    string modifiedJson = JsonConvert.SerializeObject(obj);
                    responseBody = Encoding.UTF8.GetBytes(modifiedJson);
                }

                if (request.Url.PathAndQuery.StartsWith("/profiles/page/Planning"))
                {
                    string responseBodyString = Encoding.UTF8.GetString(responseBody);
                    JObject obj = JsonConvert.DeserializeObject<JObject>(responseBodyString);

                    string modifiedJson = JsonConvert.SerializeObject(obj);

                    if (applyFuncOrNot("planning.remove-limits"))
                    {
                        string pattern = @"""Equip"":\s*\["".*?""\]";
                        string replacement = @"""Equip"": []";
                        string pattern1 = @"""GearSlotsEnabledCount"":\s*\d+";
                        string replacement1 = @"""GearSlotsEnabledCount"": 99";
                        string pattern2 = @"""GearSlotsAllowContainers"":\s*false";
                        string replacement2 = @"""GearSlotsAllowContainers"": true";
                        string pattern3 = @"""ConcealedWeaponSlotEnabled"":\s*false";
                        string replacement3 = @"""ConcealedWeaponSlotEnabled"": true";

                        // 依次执行替换
                        modifiedJson = Regex.Replace(modifiedJson, pattern1, replacement1);
                        modifiedJson = Regex.Replace(modifiedJson, pattern2, replacement2);
                        modifiedJson = Regex.Replace(modifiedJson, pattern3, replacement3);
                        modifiedJson = Regex.Replace(modifiedJson, pattern, replacement);
                    }

                    responseBody = Encoding.UTF8.GetBytes(modifiedJson);
                }

                using (Stream responseStream = response.OutputStream)
                {
                    responseStream.Write(responseBody, 0, responseBody.Length);
                }

                // Close the response streams
                response.Close();
                destinationResponse.Close();
            }
            catch (Exception e)
            {
                logger.log(e.ToString());
                response.StatusCode = 500;
                response.Close();
            }


        }

        public void Listen()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5866/");
            listener.Prefixes.Add("http://localhost:5867/"); // https://hm3-service.hitman.io

            // Start the listener
            listener.Start();
            Console.WriteLine("Listening for requests");
            Task.Factory.StartNew(() =>
            {
                // Wait for a request and process it when it arrives
                while (true)
                {

                    // Wait for a request
                    HttpListenerContext context = listener.GetContext();
                    HttpListenerRequest request = context.Request;
                    HttpListenerResponse response = context.Response;
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            ProcessRequest(request, response);
                        }
                        catch (Exception e)
                        {
                            logger.log(e.ToString());
                        }
                    });
                }
            });


        }
    }
}