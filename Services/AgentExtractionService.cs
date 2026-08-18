using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Services
{
    public class ExtractedAgents
    {
        public List<AgentRecord> Agents { get; set; } = new List<AgentRecord>();
        public string HotelId { get; set; } = "";
    }

    /// <summary>
    /// Exact port of extractAgents() - reused unmodified as instructed.
    /// Checks parsed.response.data.agents first, then
    /// parsed.hotelogix.response.data.agents, else returns an empty result.
    /// </summary>
    public static class AgentExtractionService
    {
        public static ExtractedAgents ExtractAgents(JObject parsed)
        {
            var directAgents = parsed?["response"]?["data"]?["agents"] as JArray;

            if (directAgents != null)
            {
                return new ExtractedAgents
                {
                    Agents = directAgents.Select(BuildAgentRecord).ToList(),
                    HotelId = parsed["response"]?["hotelId"]?.ToString() ?? ""
                };
            }

            var hotelogixAgents = parsed?["hotelogix"]?["response"]?["data"]?["agents"] as JArray;

            if (hotelogixAgents != null)
            {
                return new ExtractedAgents
                {
                    Agents = hotelogixAgents.Select(BuildAgentRecord).ToList(),
                    HotelId = parsed["hotelogix"]?["response"]?["hotelId"]?.ToString() ?? ""
                };
            }

            return new ExtractedAgents { Agents = new List<AgentRecord>(), HotelId = "" };
        }

        private static AgentRecord BuildAgentRecord(JToken token)
        {
            var agent = new AgentRecord
            {
                Id = token["id"]?.ToString() ?? "",
                Organization = token["organization"]?.ToString() ?? "",
                FName = token["fName"]?.ToString() ?? "",
                LName = token["lName"]?.ToString() ?? "",
                Email = token["email"]?.ToString() ?? "",
                MobileNoRaw = token["mobileNo"],
                PhoneNoRaw = token["phoneNo"],
                Code = token["code"]?.ToString() ?? "",
                HotelId = token["hotelId"]?.ToString() ?? "",
                GstinDetails = new List<GstinDetail>(),
                Addresses = new AgentAddresses()
            };

            if (token["gstinDetails"] is JArray gstArr)
            {
                foreach (var g in gstArr)
                {
                    agent.GstinDetails.Add(new GstinDetail
                    {
                        Gstin = g["gstin"]?.ToString() ?? "",
                        Name = g["name"]?.ToString() ?? "",
                        IsDefault = g["isDefault"] != null && g["isDefault"].Type == JTokenType.Boolean
                            ? g["isDefault"].Value<bool>()
                            : (bool?)null
                    });
                }
            }

            var addresses = token["addresses"];

            if (addresses != null)
            {
                var home = addresses["home"];
                var work = addresses["work"] ?? addresses["worK"];

                agent.Addresses.Home = BuildAddress(home);
                agent.Addresses.Work = BuildAddress(work);
            }

            return agent;
        }

        private static AddressInfo BuildAddress(JToken token)
        {
            if (token == null)
            {
                return new AddressInfo();
            }

            return new AddressInfo
            {
                Country = token["country"]?.ToString() ?? "",
                State = token["state"]?.ToString() ?? "",
                City = token["city"]?.ToString() ?? "",
                Zip = token["zip"]
            };
        }
    }
}
