using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.Models;
using Newtonsoft.Json.Linq;

namespace AgentSyncConsole.Services
{
    public class ExtractedCorporates
    {
        public List<CorporatesRecord> Corporates { get; set; } = new List<CorporatesRecord>();
        public string HotelId { get; set; } = "";
    }
    public static class CorporatesExtractionService
    {
        public static ExtractedCorporates ExtractCorporates(JObject parsed)
        {
            var directCorporates = parsed?["response"]?["data"]?["corporates"] as JArray;

            if (directCorporates != null)
            {
                return new ExtractedCorporates
                {
                    Corporates = directCorporates.Select(BuildCorporatesRecord).ToList(),
                    HotelId = parsed["response"]?["hotelId"]?.ToString() ?? ""
                };
            }

            var hotelogixCorporates = parsed?["hotelogix"]?["response"]?["data"]?["corporates"] as JArray;

            if (hotelogixCorporates != null)
            {
                return new ExtractedCorporates
                {
                    Corporates = hotelogixCorporates.Select(BuildCorporatesRecord).ToList(),
                    HotelId = parsed["hotelogix"]?["response"]?["hotelId"]?.ToString() ?? ""
                };
            }

            return new ExtractedCorporates { Corporates = new List<CorporatesRecord>(), HotelId = "" };
        }
            private static CorporatesRecord BuildCorporatesRecord(JToken token)
        {
            var corporates = new CorporatesRecord
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
                    corporates.GstinDetails.Add(new GstinDetail
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

                corporates.Addresses.Home = BuildAddress(home);
                corporates.Addresses.Work = BuildAddress(work);
            }

            return corporates;
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
